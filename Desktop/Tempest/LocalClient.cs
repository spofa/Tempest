﻿//
// LocalClient.cs
//
// Author:
//   Eric Maupin <me@ermau.com>
//
// Copyright (c) 2011-2013 Eric Maupin
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Tempest
{
	/// <summary>
	/// Tempest clients.
	/// </summary>
	public class LocalClient
		: MessageHandler, IClientContext, INotifyPropertyChanged
	{
		public LocalClient (IClientConnection connection, MessageTypes mtypes, bool poll = false)
		{
			if (connection == null)
				throw new ArgumentNullException ("connection");
			if (!Enum.IsDefined (typeof (MessageTypes), mtypes))
				throw new ArgumentException ("Not a valid MessageTypes value", "mtypes");

			this.messageTypes = mtypes;

			this.connection = connection;
			this.connection.Connected += OnConnectionConnected;
			this.connection.Disconnected += OnConnectionDisconnected;

			if ((this.connection.Modes & MessagingModes.Inline) == MessagingModes.Inline && (Environment.ProcessorCount == 1 || (this.connection.Modes & MessagingModes.Async) != MessagingModes.Async))
				this.mode = MessagingModes.Inline;
			else
			{
				#if NET_4
				this.mqueue = new ConcurrentQueue<MessageEventArgs>();
				#else
				this.mqueue = new Queue<MessageEventArgs>();
				#endif
				this.connection.MessageReceived += ConnectionOnMessageReceived;
				this.mode = MessagingModes.Async;
			}

			this.polling = poll;
		}

		public event PropertyChangedEventHandler PropertyChanged;

		/// <summary>
		/// Raised when the client connects.
		/// </summary>
		public event EventHandler<ClientConnectionEventArgs> Connected;

		/// <summary>
		/// Raised with the client is disconnected.
		/// </summary>
		public event EventHandler<ClientDisconnectedEventArgs> Disconnected;

		public IClientConnection Connection
		{
			get { return this.connection; }
		}

		/// <summary>
		/// Gets whether the client is currently connected or not.
		/// </summary>
		public virtual bool IsConnected
		{
			get { return this.connection.IsConnected; }
		}

		/// <summary>
		/// Attempts to asynchronously connect to <paramref name="target"/>.
		/// </summary>
		/// <param name="target">The target to connect to.</param>
		/// <exception cref="ArgumentNullException"><paramref name="target"/> is <c>null</c>.</exception>
		public virtual Task<ClientConnectionResult> ConnectAsync (Target target)
		{
			if (target == null)
				throw new ArgumentNullException ("target");

			if (this.mode == MessagingModes.Async)
			{
				var newWait = new AutoResetEvent (false);
				AutoResetEvent oldWait = Interlocked.Exchange (ref this.mwait, newWait);
				if (oldWait != null)
					oldWait.Set();
			}

			return this.connection.ConnectAsync (target, this.messageTypes);
		}

		/// <summary>
		/// Disconnects from the server.
		/// </summary>
		public virtual Task DisconnectAsync()
		{
			return Disconnect (ConnectionResult.Custom, "Requested");
		}

		public virtual Task DisconnectAsync (ConnectionResult reason, string customReason = null)
		{
			return Disconnect (reason, customReason);
		}

		/// <summary>
		/// Manually polls the connection and invokes message handlers.
		/// </summary>
		public void Poll()
		{
			if (!IsConnected)
				return;

			List<MessageEventArgs> messages;
			if (this.mode == MessagingModes.Async)
			{
				messages = new List<MessageEventArgs> (mqueue.Count * 2);

				#if NET_4
				MessageEventArgs e;
				while (this.mqueue.TryDequeue (out e))
					messages.Add (e);
				#else
				lock (this.mqueue)
				{
					while (this.mqueue.Count > 0)
						messages.Add (this.mqueue.Dequeue());
				}
				#endif
			}
			else
				messages = this.connection.Tick().ToList();

			for (int i = 0; i < messages.Count; ++i)
			{
				var m = messages[i];
				var mhandlers = GetHandlers (m.Message);
				if (mhandlers == null)
					continue;

				for (int n = 0; n < mhandlers.Count; ++n)
					mhandlers[n] (m);
			}

			if (this.disconnecting)
				DisconnectAsync();
		}

		private bool disconnecting;
		private readonly IClientConnection connection;
		private readonly MessagingModes mode;
		private readonly bool polling;

		#if NET_4
		private readonly ConcurrentQueue<MessageEventArgs> mqueue;
		#else
		private readonly Queue<MessageEventArgs> mqueue;
		#endif

		private AutoResetEvent mwait;
		private Thread messageRunner;
		private volatile bool running;
		private readonly MessageTypes messageTypes;

		private Task Disconnect (ConnectionResult reason, string customReason)
		{
			this.disconnecting = true;
			Task task = this.connection.DisconnectAsync (reason, customReason);

			this.running = false;

			if (this.mode == MessagingModes.Async)
			{
				AutoResetEvent wait = Interlocked.Exchange (ref this.mwait, null);
				if (wait != null)
					wait.Set();

				#if NET_4
				MessageEventArgs e;
				while (this.mqueue.TryDequeue (out e)) ;
				#else
				lock (this.mqueue)
					this.mqueue.Clear();
				#endif
			}

			Thread runner = Interlocked.Exchange (ref this.messageRunner, null);
			if (runner != null)
				task = task.ContinueWith (t => runner.Join());
			
			this.disconnecting = false;
			return task;
		}

		private void ConnectionOnMessageReceived (object sender, MessageEventArgs e)
		{
			lock (this.mqueue)
				this.mqueue.Enqueue (e);

			AutoResetEvent wait = this.mwait;
			if (wait != null)
				wait.Set();
		}

		private void InlineMessageRunner()
		{
			#if NET_4
			SpinWait wait = new SpinWait();
			#endif

		    while (this.running)
		    {
				List<MessageEventArgs> messages = this.connection.Tick().ToList();
				if (this.running && messages.Count > 0)
				{
					#if NET_4
					wait.Reset();
					#endif

					foreach (MessageEventArgs e in messages)
					{
						if (!this.running)
							break;

						var mhandlers = GetHandlers (e.Message);
						if (mhandlers == null)
							continue;

						for (int i = 0; i < mhandlers.Count; ++i)
							mhandlers[i] (e);
					}
				}

				if (this.disconnecting)
				{
					DisconnectAsync();
					return;
				}

				#if NET_4
				wait.SpinOnce();
				#else
				Thread.Sleep (1);
				#endif
		    }
		}

		private void AsyncMessageRunner ()
		{
			#if NET_4
			ConcurrentQueue<MessageEventArgs> q = this.mqueue;			
			#else
			Queue<MessageEventArgs> q = this.mqueue;
			#endif		

			while (this.running)
			{
				while (q.Count > 0 && this.running)
				{
					MessageEventArgs e;
					#if NET_4
					if (!q.TryDequeue (out e))
						continue;
					#else
					lock (q)
					{
						if (q.Count == 0)
							continue;

						e = q.Dequeue();
					}
					#endif

					List<Action<MessageEventArgs>> mhandlers = GetHandlers (e.Message);
					if (mhandlers == null)
						continue;

					for (int i = 0; i < mhandlers.Count; ++i)
						mhandlers[i] (e);
				}

				if (this.disconnecting)
				{
					DisconnectAsync();
					return;
				}

				if (q.Count == 0)
				{
					AutoResetEvent wait = this.mwait;
					if (wait != null)
						wait.WaitOne();
				}
			}
		}

		private void OnConnectionDisconnected (object sender, DisconnectedEventArgs e)
		{
			OnPropertyChanged (new PropertyChangedEventArgs ("IsConnected"));
			Disconnect (e.Result, e.CustomReason);
			OnDisconnected (new ClientDisconnectedEventArgs (e.Result == ConnectionResult.Custom, e.Result, e.CustomReason));
		}

		private void OnConnectionConnected (object sender, ClientConnectionEventArgs e)
		{
			OnPropertyChanged (new PropertyChangedEventArgs ("IsConnected"));

			this.running = true;

			if (!this.polling)
			{
				while (this.messageRunner != null)
					Thread.Sleep (0);

				Thread runner = (this.mode == MessagingModes.Inline) ? new Thread (InlineMessageRunner) : new Thread (AsyncMessageRunner);
				runner.Name = "Client Message Runner";
				runner.IsBackground = true;
				runner.Start();

				this.messageRunner = runner;
			}

			OnConnected (e);
		}

		protected virtual void OnPropertyChanged (PropertyChangedEventArgs e)
		{
			var changed = PropertyChanged;
			if (changed != null)
				changed (this, e);
		}

		protected virtual void OnConnected (ClientConnectionEventArgs e)
		{
			EventHandler<ClientConnectionEventArgs> handler = Connected;
			if (handler != null)
				handler (this, e);
		}

		protected virtual void OnDisconnected (ClientDisconnectedEventArgs e)
		{
			var handler = Disconnected;
			if (handler != null)
				handler (this, e);
		}
	}

	public class ClientDisconnectedEventArgs
		: EventArgs
	{
		public ClientDisconnectedEventArgs (bool requested, ConnectionResult reason, string customReason)
		{
			Requested = requested;
			Reason = reason;
			CustomReason = customReason;
		}

		/// <summary>
		/// Gets whether the disconnection was requested by LocalClient.
		/// </summary>
		public bool Requested
		{
			get;
			private set;
		}

		public ConnectionResult Reason
		{
			get;
			private set;
		}

		public string CustomReason
		{
			get;
			private set;
		}
	}
}