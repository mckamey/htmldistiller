#region License
/*---------------------------------------------------------------------------------*\

	Distributed under the terms of an MIT-style license:

	The MIT License

	Copyright (c) 2006-2009 Stephen M. McKamey

	Permission is hereby granted, free of charge, to any person obtaining a copy
	of this software and associated documentation files (the "Software"), to deal
	in the Software without restriction, including without limitation the rights
	to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
	copies of the Software, and to permit persons to whom the Software is
	furnished to do so, subject to the following conditions:

	The above copyright notice and this permission notice shall be included in
	all copies or substantial portions of the Software.

	THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
	IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
	FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
	AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
	LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
	OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
	THE SOFTWARE.

\*---------------------------------------------------------------------------------*/
#endregion License

using System;
using System.Collections.Generic;

namespace JsonFx.BuildTools.Collections
{
	/// <summary>
	/// A compact data structure for storing large sets of URLs with associated values
	/// </summary>
	/// <typeparam name="TValue"></typeparam>
	/// <remarks>
	/// http://en.wikipedia.org/wiki/Trie
	/// </remarks>
	public class CharTrieNode<TValue> : ITrieNode<char, TValue>
	{
		#region Constants

		/// <summary>
		/// According to RFC 1738:
		/// 
		/// "URLs are written only with the graphic printable characters of
		/// the US-ASCII coded character set. The octets 80-FF hexadecimal
		/// are not used in US-ASCII, and the octets 00-1F and 7F hexadecimal
		/// represent control characters; these must be encoded."
		/// </summary>
		private const int CharsetStart = 0x20;
		private const int CharsetEnd = 0x7E;
		private const int CharsetLength = CharsetEnd-CharsetStart+1;

		#endregion Constants

		#region Fields

		public readonly bool CaseSensitive;
		private readonly ITrieNode<char, TValue>[] Children = new ITrieNode<char, TValue>[CharsetLength];
		private TValue value = default(TValue);

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		public CharTrieNode() : this(false)
		{
		}

		/// <summary>
		/// Ctor.
		/// </summary>
		/// <param name="capacity"></param>
		public CharTrieNode(bool caseSensitive)
		{
			this.CaseSensitive = caseSensitive;
		}

		#endregion Init

		#region ITrieNode<char,TValue> Members

		public ITrieNode<char, TValue> this[char key]
		{
			get
			{
				if (!this.CaseSensitive)
				{
					key = Char.ToLowerInvariant(key);
				}
				return this.Children[this.MapKey(key)];
			}
			set
			{
				if (!this.CaseSensitive)
				{
					key = Char.ToLowerInvariant(key);
				}
				this.Children[this.MapKey(key)] = value;
			}
		}

		public TValue Value
		{
			get { return this.value; }
			set { this.value = value; }
		}

		public bool HasValue
		{
			get
			{
				return !EqualityComparer<TValue>.Default.Equals(this.value, default(TValue));
			}
		}

		public bool Contains(char key)
		{
			return (this[key] != null);
		}

		#endregion ITrieNode<char,TValue> Members

		#region Methods

		protected virtual int MapKey(char key)
		{
			if (key < CharsetStart ||
					key > CharsetEnd)
			{
				throw new ArgumentOutOfRangeException(
					String.Format("Key cannot be outside of ASCII range 0x{0:x2}-0x{1:x2}",
					CharsetStart,
					CharsetEnd));
			}
			if (!this.CaseSensitive)
			{
				key = Char.ToLowerInvariant(key);
			}
			return (int)(key-CharsetStart);
		}

		#endregion Methods
	}

	public class StringDictionary<TValue> //: IDictionary<string, TValue>
	{
		#region Fields

		private readonly CharTrieNode<TValue> root;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		public StringDictionary() : this(false)
		{
		}

		/// <summary>
		/// Ctor.
		/// </summary>
		/// <param name="capacity"></param>
		public StringDictionary(bool caseSensitive)
		{
			this.root = new CharTrieNode<TValue>(caseSensitive);
		}

		#endregion Init

		#region Properties

		public TValue this[string key]
		{
			get { return this.GetNodeValue(key); }
			set { this.SetNodeValue(key, value); }
		}

		#endregion Properties

		#region Methods

		public bool ContainsKey(string key)
		{
			return !EqualityComparer<TValue>.Default.Equals(this.GetNodeValue(key), default(TValue));
		}

		private void SetNodeValue(string key, TValue value)
		{
			CharTrieNode<TValue> node = this.root;

			// build out the path for value
			foreach (char ch in key)
			{
				if (!node.Contains(ch))
				{
					node[ch] = new CharTrieNode<TValue>(node.CaseSensitive);
				}

				node = (CharTrieNode<TValue>)node[ch];
			}

			// at the end of the Prefix is the Index
			node.Value = value;
		}

		private TValue GetNodeValue(string key)
		{
			CharTrieNode<TValue> node = this.root;

			// build out the path for value
			foreach (char ch in key)
			{
				if (!node.Contains(ch))
				{
					return default(TValue);
				}

				node = (CharTrieNode<TValue>)node[ch];
			}

			// at the end of the Prefix is the Index
			return node.Value;
		}

		#endregion Methods
	}
}
