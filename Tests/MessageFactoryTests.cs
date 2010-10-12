﻿//
// MessageFactoryTests.cs
//
// Author:
//   Eric Maupin <me@ermau.com>
//
// Copyright (c) 2010 Eric Maupin
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
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Tempest.Tests
{
	[TestFixture]
	public class MessageFactoryTests
	{
		private MessageFactory factory;

		[SetUp]
		public void Setup()
		{
			this.factory = new MessageFactory();
		}

		[Test]
		public void DiscoverNull()
		{
			Assert.Throws<ArgumentNullException> (() => this.factory.Discover (null));
		}

		[Test]
		public void Discover()
		{
			this.factory.Discover();

			Message m = this.factory.Create (1);
			Assert.IsNotNull (m);
			Assert.That (m, Is.TypeOf<MockMessage>());
		}

		[Test]
		public void DiscoverAssembly()
		{
			this.factory.Discover (typeof(MessageFactoryTests).Assembly);

			Message m = this.factory.Create (1);
			Assert.IsNotNull (m);
			Assert.That (m, Is.TypeOf<MockMessage>());
		}

		[Test]
		public void DiscoverAssemblyNothing()
		{
			this.factory.Discover (typeof(string).Assembly);

			Message m = this.factory.Create (1);
			Assert.IsNull (m);
		}

		[Test]
		public void RegisterNull()
		{
			#if !SAFE
			Assert.Throws<ArgumentNullException> (() => this.factory.Register ((IEnumerable<Type>)null));
			#endif

			Assert.Throws<ArgumentNullException> (() => this.factory.Register ((IEnumerable<KeyValuePair<Type, Func<Message>>>)null));
		}

		private class PrivateMessage
			: Message
		{
			public PrivateMessage (ushort type)
				: base (type)
			{
			}

			public override void Serialize(IValueWriter writer)
			{
			}

			public override void Deserialize(IValueReader reader)
			{
			}
		}

		[Test]
		public void RegisterTypeInvalid()
		{
			Assert.Throws<ArgumentException> (() => this.factory.Register (new[] { typeof (PrivateMessage) }));
			Assert.Throws<ArgumentException> (() => this.factory.Register (new[] { typeof (int) }));
			Assert.Throws<ArgumentException> (() => this.factory.Register (new[] { typeof (string) }));
		}

		[Test]
		public void RegisterTypeDuplicates()
		{
			Assert.Throws<ArgumentException> (() => this.factory.Register (new[] { typeof (MockMessage), typeof (MockMessage) }));
		}

		[Test]
		public void RegisterTypeAndCtorsInvalid()
		{
			Assert.Throws<ArgumentException> (() =>
				this.factory.Register (new[] { new KeyValuePair<Type, Func<Message>> (typeof (string), () => new MockMessage()) }));
			Assert.Throws<ArgumentException> (() =>
				this.factory.Register (new[] { new KeyValuePair<Type, Func<Message>> (typeof (int), () => new MockMessage()) }));
		}

		[Test]
		public void RegisterTypeAndCtorsDuplicates()
		{
			Assert.Throws<ArgumentException> (() =>
				this.factory.Register (new[]
				{
					new KeyValuePair<Type, Func<Message>> (typeof (MockMessage), () => new MockMessage()),
					new KeyValuePair<Type, Func<Message>> (typeof (MockMessage), () => new MockMessage()),
				}));
		}

		[Test]
		public void RegisterType()
		{
			this.factory.Register (new[] { typeof(MockMessage) });

			Message m = this.factory.Create (1);
			Assert.IsNotNull (m);
			Assert.That (m, Is.TypeOf<MockMessage>());
		}

		[Test]
		public void RegisterTypeWithCtor()
		{
			this.factory.Register (new []
			{
				new KeyValuePair<Type, Func<Message>> (typeof(MockMessage), () => new MockMessage()), 
				new KeyValuePair<Type, Func<Message>> (typeof(PrivateMessage), () => new PrivateMessage (2)), 
				new KeyValuePair<Type, Func<Message>> (typeof(PrivateMessage), () => new PrivateMessage (3)), 
			});

			Message m = this.factory.Create (1);
			Assert.IsNotNull (m);
			Assert.That (m, Is.TypeOf<MockMessage>());

			m = this.factory.Create (2);
			Assert.IsNotNull (m);
			Assert.AreEqual (2, m.MessageType);
			Assert.That (m, Is.TypeOf<PrivateMessage>());

			m = this.factory.Create (3);
			Assert.IsNotNull (m);
			Assert.AreEqual (3, m.MessageType);
			Assert.That (m, Is.TypeOf<PrivateMessage>());
		}
	}
}