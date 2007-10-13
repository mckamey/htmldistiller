using System;
using System.Collections.Generic;

namespace BuildTools.Collections
{
	/// <summary>
	/// A compact data structure for storing large sets of URLs with associated values
	/// </summary>
	/// <typeparam name="TValue"></typeparam>
	/// <remarks>
	/// http://en.wikipedia.org/wiki/Trie
	/// </remarks>
	public class UrlTrieNode<TValue> : ITrieNode<char, TValue>
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
		private const int CharsetLength = CharsetEnd-CharsetStart;

		#endregion Constants

		#region Fields

		private readonly bool CaseSensitive;
		private readonly UrlTrieNode<TValue>[] Children = new UrlTrieNode<TValue>[CharsetLength];
		private TValue value = default(TValue);

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		public UrlTrieNode() : this(false)
		{
		}

		/// <summary>
		/// Ctor.
		/// </summary>
		/// <param name="capacity"></param>
		public UrlTrieNode(bool caseSensitive)
		{
			this.CaseSensitive = caseSensitive;
		}

		#endregion Init

		#region ITrieNode<char,TValue> Members

		public ITrieNode<char, TValue> this[char key]
		{
			get
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
				return this.Children[key];
			}
		}

		public TValue Value
		{
			get { return this.value; }
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
	}
}
