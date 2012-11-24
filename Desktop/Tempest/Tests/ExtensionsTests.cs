﻿//
// ExtensionsTests.cs
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
using NUnit.Framework;

namespace Tempest.Tests
{
	[TestFixture]
	public class ExtensionsTests
	{
		[Test]
		public void ReadWriteUniversalDate()
		{
			byte[] buffer = new byte[20480];
			var writer = new BufferValueWriter (buffer);

			DateTime d = DateTime.Now;

			writer.WriteUniversalDate (d);
			writer.Flush();

			var reader = new BufferValueReader (buffer);

			Assert.AreEqual (d.ToUniversalTime(), reader.ReadUniversalDate());
		}

		[Test]
		public void ReadWrite7BitInt()
		{
			var writer = new BufferValueWriter (new byte[20480]);

			writer.Write7BitEncodedInt (Int32.MinValue);
			writer.Write7BitEncodedInt (0);
			writer.Write7BitEncodedInt (Int32.MaxValue);
			writer.Flush();

			var reader = new BufferValueReader (writer.Buffer);

			Assert.AreEqual (Int32.MinValue, reader.Read7BitEncodedInt());
			Assert.AreEqual (0, reader.Read7BitEncodedInt());
			Assert.AreEqual (Int32.MaxValue, reader.Read7BitEncodedInt());
		}
	}
}