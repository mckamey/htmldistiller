#region BuildTools License
/*---------------------------------------------------------------------------------*\

	BuildTools distributed under the terms of an MIT-style license:

	The MIT License

	Copyright (c) 2006-2008 Stephen M. McKamey

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
#endregion BuildTools License

using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using BuildTools.HtmlDistiller.Filters;
using BuildTools.HtmlDistiller.Writers;

namespace BuildTools.HtmlDistiller
{
	/// <summary>
	/// Parses HTML, repairing and scrubbing against various whitelist filters.
	/// </summary>
	/// <remarks>
	/// Note: this class is thread-safe (all external changes are locked first)
	/// </remarks>
	public class HtmlDistiller
	{
		#region ParseState

		/// <summary>
		/// Exception which halts parsing preserving last sync point
		/// </summary>
		private class UnexpectedEofException : System.IO.EndOfStreamException
		{
		}

		#endregion ParseState

		#region Constants

		public const char NullChar = '\0';
		private const char CRChar = '\r';
		private const char LFChar = '\n';
		private const char OpenTagChar = '<';
		private const char CloseTagChar = '>';
		private const char EndTagChar = '/';
		private const char AttrDelimChar = '=';
		private const char SingleQuoteChar = '\'';
		private const char DoubleQuoteChar = '\"';
		private const char StylePropChar = ':';
		private const char StyleDelimChar = ';';
		private const char AlphaStartChar = 'a';
		private const char AlphaEndChar = 'z';
		private const char NumStartChar = '0';
		private const char NumEndChar = '9';
		private const char AsciiHighChar = (char)0x7F;

		public const char EntityStartChar = '&';
		private const char EntityEndChar = ';';
		private const char EntityNumChar = '#';
		private const char EntityHexChar = 'x';
		private const char HexStartChar = 'A';
		private const char HexEndChar = 'F';

		private const string StyleAttrib = "style";
		private const string EllipsisEntity = "&hellip;";
		private const string Ellipsis = "...";
		private const string LessThanEntity = "&lt;";

		#endregion Constants

		#region Fields

		private readonly object SyncLock = new object();

		private string source = String.Empty;
		private IHtmlFilter htmlFilter;
		private IHtmlWriter htmlWriter;
		private bool isInitialized = false;
		private int maxLength = 0;
		private bool normalizeWhitespace = false;
		private bool balanceTags = true;
		private bool encodeNonAscii = true;
		private bool incrementalParsing = false;

		private int index;		// current char in source
		private int start;		// last written char in source
		private int textSize;	// length of plain text processed
		private int syncPoint;	// last sync point (for incremental parsing)
		private Stack<HtmlTag> openTags;
		private HtmlTaxonomy taxonomy = HtmlTaxonomy.None;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor.
		/// </summary>
		public HtmlDistiller()
			: this(0, null)
		{
		}

		/// <summary>
		/// Ctor.
		/// </summary>
		/// <param name="text">the text to parse</param>
		public HtmlDistiller(int maxLength)
			: this(maxLength, null)
		{
		}

		/// <summary>
		/// Ctor.
		/// </summary>
		/// <param name="text">the text to parse</param>
		/// <param name="filter"></param>
		public HtmlDistiller(int maxLength, IHtmlFilter filter)
		{
			this.maxLength = maxLength;
			this.htmlFilter = filter;
		}

		#endregion Init

		#region Properties

		/// <summary>
		/// Gets and sets the IHtmlFilter used in parsing
		/// </summary>
		public IHtmlFilter HtmlFilter
		{
			get { return this.htmlFilter; }
			set
			{
				lock (this.SyncLock)
				{
					this.htmlFilter = value;
				}
			}
		}

		/// <summary>
		/// Gets and sets the IHtmlWriter used for output
		/// </summary>
		public IHtmlWriter HtmlWriter
		{
			get { return this.htmlWriter; }
			set
			{
				lock (this.SyncLock)
				{
					this.htmlWriter = value;
				}
			}
		}

		/// <summary>
		/// Gets and sets the maximum number of text chars (tags don't count)
		/// </summary>
		public int MaxLength
		{
			get { return this.maxLength; }
			set
			{
				lock (this.SyncLock)
				{
					this.maxLength = value;
				}
			}
		}

		/// <summary>
		/// Gets and sets if whitespace should be normalized
		/// </summary>
		public bool NormalizeWhitespace
		{
			get { return this.normalizeWhitespace; }
			set
			{
				lock (this.SyncLock)
				{
					this.normalizeWhitespace = value;
				}
			}
		}

		/// <summary>
		/// Gets and sets if tags should be auto-balance
		/// </summary>
		public bool BalanceTags
		{
			get { return this.balanceTags; }
			set
			{
				lock (this.SyncLock)
				{
					this.balanceTags = value;
				}
			}
		}

		/// <summary>
		/// Gets and sets if non-ASCII chars should be encoded as HTML entities
		/// </summary>
		public bool EncodeNonAscii
		{
			get { return this.encodeNonAscii; }
			set
			{
				lock (this.SyncLock)
				{
					this.encodeNonAscii = value;
				}
			}
		}

		/// <summary>
		/// Gets and sets the source text
		/// </summary>
		public string Source
		{
			get { return this.source; }
		}

		/// <summary>
		/// Gets a value indicating the taxonomy of tags rendered
		/// </summary>
		public HtmlTaxonomy Taxonomy
		{
			get { return this.taxonomy; }
		}

		#endregion Properties

		#region Parse Properties

		/// <summary>
		/// Gets the current character.
		/// </summary>
		private char Current
		{
			get
			{
				if (this.IsEOF)
				{
					return NullChar;
				}
				return this.source[this.index];
			}
		}

		/// <summary>
		/// Gets if at the end of source text.
		/// </summary>
		private bool IsEOF
		{
			get
			{
				// allow the text to arbitrarily end short
				if (this.MaxLength > 0 && this.textSize >= this.MaxLength)
				{
					return true;
				}

				return (this.index >= this.source.Length);
			}
		}

		#endregion Parse Properties

		#region Parse Methods

		/// <summary>
		/// Starts parsing to be performed incrementally
		/// </summary>
		/// <remarks>
		/// There is a performance hit for parsing in chunks.
		/// </remarks>
		public void BeginIncrementalParsing()
		{
			lock (this.SyncLock)
			{
				this.isInitialized = false;
				this.incrementalParsing = true;
			}
		}

		/// <summary>
		/// Stops incremental parsing and completes tag balancing, etc.
		/// </summary>
		/// <returns>the output text</returns>
		public void EndIncrementalParsing()
		{
			lock (this.SyncLock)
			{
				this.incrementalParsing = false;
				this.Parse(null);
			}
		}

		/// <summary>
		/// Parses the source using the current settings.
		/// </summary>
		/// <param name="source">the source to be parsed</param>
		public void Parse(string html)
		{
			lock (this.SyncLock)
			{
				try
				{
					this.Init(html);

					while (!this.IsEOF)
					{
						// store syncPoint
						this.syncPoint = this.index;

						char ch = this.Current;
						if (ch == OpenTagChar)
						{
							#region found potential tag

							// write out all before LessThan
							this.WriteBuffer();

							HtmlTag tag = this.ParseTag();
							if (tag != null)
							{
								switch (tag.TagType)
								{
									case HtmlTagType.Comment:
									case HtmlTagType.FullTag:
									{
										this.RenderTag(tag);
										break;
									}
									case HtmlTagType.BeginTag:
									{
										// keep copy for pairing
										this.openTags.Push(tag);
										this.RenderTag(tag);
										break;
									}
									case HtmlTagType.EndTag:
									{
										if (!this.balanceTags)
										{
											// render regardless
											this.RenderTag(tag);
											break;
										}

										if (this.openTags.Count == 0)
										{
											// no open tags so no need for EndTag
											break;
										}

										// check for matching pair
										HtmlTag openTag = this.openTags.Pop();
										if (tag.TagName == openTag.TagName)
										{
											// found match
											this.RenderTag(tag);
											break;
										}

										#region repair mismatched tags

										// if isn't in stack then it doesn't help to attempt to repair
										if (!this.openTags.Contains(tag.CreateOpenTag()))
										{
											// put the tag back on
											this.openTags.Push(openTag);

											// ignore end tag
											break;
										}
										else
										{
											// try to repair mismatch
											Stack<HtmlTag> mismatched = new Stack<HtmlTag>(this.openTags.Count);

											do
											{
												// close mismatched tags
												this.RenderCloseTag(openTag);

												// store for re-opening
												mismatched.Push(openTag);
												if (this.openTags.Count == 0)
												{
													// no match found
													openTag = null;
													break;
												}

												// get next
												openTag = this.openTags.Pop();
											} while (tag.TagName != openTag.TagName);

											if (openTag != null)
											{
												// found matching tag
												this.RenderTag(tag);
											}

											// reopen mismatched tags
											while (mismatched.Count > 0)
											{
												openTag = mismatched.Pop();
												this.openTags.Push(openTag);
												this.RenderTag(openTag);
											}
										}
										break;

										#endregion repair mismatched tags
									}
									default:
									{
										break;
									}
								}
							}
							else
							{
								#region encode LessThan char

								if (this.EncodeNonAscii)
								{
									// encode LessThan char
									this.HtmlWriter.WriteLiteral(LessThanEntity);

									// remove from stream
									this.EmptyBuffer(1);
								}
								else
								{
									this.Advance();
								}

								// count toward total text length
								this.IncTextCount();

								#endregion encode LessThan char
							}

							#endregion found potential tag
						}
						else if (this.normalizeWhitespace && (Char.IsWhiteSpace(ch) || Char.IsControl(ch)))
						{
							#region normalize whitespace

							while ((Char.IsWhiteSpace(ch) || Char.IsControl(ch)) && !this.IsEOF)
							{
								if (ch == CRChar)
								{
									#region normalize line endings (CR/CRLF -> LF)

									// write out all before CR
									this.WriteBuffer();

									if (this.Peek(1) != LFChar)
									{
										// just CR so replace CR with LF
										this.HtmlWriter.WriteLiteral(LFChar);

										// count toward total text length
										this.IncTextCount();
									}

									// skip CR
									this.EmptyBuffer(1);

									#endregion normalize line endings (CR/CRLF -> LF)
								}
								else if (ch == LFChar)
								{
									#region limit line endings (no more than 2 LF)

									// write out all before LF
									this.WriteBuffer();

									char prev1 = this.PrevChar(1);
									char prev2 = this.PrevChar(2);
									if ((prev1 == LFChar || (prev1 == NullChar && !this.incrementalParsing)) &&
										(prev2 == LFChar || (prev2 == NullChar && !this.incrementalParsing)))
									{
										// skip 3rd+ LFs
										while (true)
										{
											this.Advance();
											ch = this.Current;
											if (ch != LFChar &&
												ch != CRChar)
											{
												break;
											}
										}
										this.EmptyBuffer();
									}
									else
									{
										// keep going, will copy out as larger buffer
										this.Advance();

										// count towards text chars
										this.IncTextCount();
									}

									#endregion limit line endings (no more than 2 LF)
								}
								else
								{
									#region normalize spaces and tabs

									char prev1 = this.PrevChar(1);
									if (Char.IsWhiteSpace(prev1) ||
										(prev1 == NullChar && !this.incrementalParsing))
									{
										// write out all before extra whitespace
										this.WriteBuffer();

										// eat extra whitespace
										this.EmptyBuffer(1);
									}
									else
									{
										// keep going, will copy out as larger buffer
										this.Advance();

										// count towards text chars
										this.IncTextCount();
									}

									#endregion normalize spaces and tabs
								}

								ch = this.Current;
							}

							#endregion normalize whitespace
						}
						else if (this.EncodeNonAscii && (ch > AsciiHighChar || (Char.IsControl(ch) && !Char.IsWhiteSpace(ch))))
						{
							#region encode non-ASCII chars

							// write out all before non-ASCII char
							this.WriteBuffer();

							// encode the non-ASCII char
							string entity = HtmlDistiller.EncodeHtmlEntity(ch);
							this.HtmlWriter.WriteLiteral(entity);

							// remove char from stream
							this.EmptyBuffer(1);

							// count toward total text length
							this.IncTextCount();

							#endregion encode non-ASCII chars
						}
						else if (!this.EncodeNonAscii && (ch == EntityStartChar))
						{
							#region decode HTML entities

							char entityChar;

							// encode the HTML entity
							int entityLength = HtmlDistiller.DecodeHtmlEntity(this.source, this.index, out entityChar);
							if (entityLength > 1)
							{
								this.WriteBuffer();

								// output decoded char
								this.HtmlWriter.WriteLiteral(entityChar);

								// remove char from stream
								this.EmptyBuffer(entityLength);
							}
							else
							{
								// no entity found, simply treat as normal char
								this.Advance();
							}

							// count toward total text length
							this.IncTextCount();

							#endregion decode HTML entities
						}
						else
						{
							#region all other chars

							// keep going, will copy out as larger buffer
							this.Advance();

							// count towards text chars
							this.IncTextCount();

							#endregion all other chars
						}
					}

					this.WriteBuffer();

					// reset syncPoint
					this.syncPoint = -1;

					#region close any open tags

					if (this.balanceTags && !this.incrementalParsing)
					{
						while (this.openTags.Count > 0)
						{
							// write out any unclosed tags
							HtmlTag tag = this.openTags.Pop();
							this.RenderCloseTag(tag);
						}
					}

					#endregion close any open tags
				}
				catch (UnexpectedEofException)
				{
					// nothing needs to be done
					// the source is preserved via last sync point
				}

				if (this.MaxLength > 0 && this.textSize >= this.MaxLength)
				{
					// source was cut off so add ellipsis
					this.HtmlWriter.WriteLiteral(this.EncodeNonAscii ? EllipsisEntity : Ellipsis);
				}

				if (!this.incrementalParsing)
				{
					this.isInitialized = false;
				}
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <returns>null if no tag was found (e.g. just LessThan char)</returns>
		private HtmlTag ParseTag()
		{
			HtmlTag tag = this.ParseComment("<%", "%>");
			if (tag != null)
			{
				// common ASP/JSP script found
				return tag;
			}
			tag = this.ParseComment("<!--", "-->");
			if (tag != null)
			{
				// standard HTML/XML/SGML comment found
				return tag;
			}
			tag = this.ParseComment("<![CDATA[", "]]>");
			if (tag != null)
			{
				// CDATA section found
				return tag;
			}
			tag = this.ParseComment("<!", ">");
			if (tag != null)
			{
				// SGML processing instruction (usually DOCTYPE)
				return tag;
			}
			tag = this.ParseComment("<?", "?>");
			if (tag != null)
			{
				// XML/SGML processing instruction (usually XML declaration)
				return tag;
			}

			char ch = Char.ToLowerInvariant(this.Peek(1));
			if ((ch < AlphaStartChar || ch > AlphaEndChar) &&
				(ch != EndTagChar) && (ch != NullChar))
			{
				// not a tag, treat as LessThan char
				return null;
			}

			// remove tag open char
			this.EmptyBuffer(1);

			while (!this.IsEOF)
			{
				// tag name chars
				this.Advance();

				ch = Char.ToLowerInvariant(this.Current);
				if ((ch < AlphaStartChar || ch > AlphaEndChar) &&
					(ch < NumStartChar || ch > NumEndChar))
				{
					break;
				}
			}

			tag = new HtmlTag(this.FlushBuffer());

			this.ParseSyncPoint();

			this.ParseAttributes(tag);

			if (this.Current == CloseTagChar)
			{
				// remove GreaterThan char from source
				this.EmptyBuffer(1);
			}

			return tag;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <returns>null if no comment found</returns>
		/// <param name="startDelim"></param>
		/// <param name="endDelim"></param>
		/// <remarks>
		/// This supports standard comments, DocType declarations, and CDATA sections.
		/// </remarks>
		private HtmlTag ParseComment(string startDelim, string endDelim)
		{
			int i=0;
			for (i=0; i<startDelim.Length; i++)
			{
				if (this.Peek(i) != startDelim[i])
				{
					return null;
				}
			}

			// consume LessThan
			this.EmptyBuffer(1);

			string commentName = this.FlushBuffer(startDelim.Length-1);

			i = 0;
			while (!this.IsEOF)
			{
				if (this.Peek(i) == endDelim[i])
				{
					i++;
					if (i == endDelim.Length)
					{
						break;
					}
				}
				else
				{
					i = 0;

					// add to comment contents
					this.Advance();
				}
			}

			this.ParseSyncPoint();

			string contents = this.FlushBuffer();
			if (!this.IsEOF)
			{
				this.EmptyBuffer(endDelim.Length);
			}

			HtmlTag comment = new HtmlTag(commentName);
			if (!String.IsNullOrEmpty(contents))
			{
				comment.Attributes[HtmlTag.Key_Contents] = contents;
			}
			comment.Attributes[HtmlTag.Key_EndDelim] = endDelim.Substring(0, endDelim.Length-1);

			return comment;
		}

		private void ParseAttributes(HtmlTag tag)
		{
			char ch = this.Current;

			while (!this.IsEOF &&
				ch != CloseTagChar &&
				ch != OpenTagChar)
			{
				string name = this.ParseAttributeName();

				this.ParseSyncPoint();

				string value = String.Empty;

				ch = this.Current;
				if (ch != CloseTagChar &&
					ch != OpenTagChar)
				{
					// Get the value(if any)
					value = this.ParseAttributeValue();
				}

				this.ParseSyncPoint();

				if (!String.IsNullOrEmpty(name))
				{
					if (StyleAttrib.Equals(name, StringComparison.InvariantCultureIgnoreCase))
					{
						this.ParseStyles(tag, value);
					}
					else
					{
						tag.Attributes[name] = value;
					}
				}

				ch = this.Current;
			}
		}

		private string ParseAttributeName()
		{
			this.SkipWhiteSpace();

			char ch = this.Current;
			if (ch == EndTagChar)
			{
				this.EmptyBuffer(1);
				return String.Empty;
			}

			while (!this.IsEOF)
			{
				ch = this.Current;
				if ((ch == AttrDelimChar) ||
					(ch == CloseTagChar) ||
					(ch == OpenTagChar) ||
					Char.IsWhiteSpace(ch))
				{
					break;
				}

				// add to attribute name
				if (ch != OpenTagChar)
				{
					this.Advance();
				}
			}

			return this.FlushBuffer();
		}

		private string ParseAttributeValue()
		{
			this.SkipWhiteSpace();

			char ch = this.Current;
			if (ch != AttrDelimChar)
			{
				return String.Empty;
			}

			this.EmptyBuffer(1);
			this.SkipWhiteSpace();

			char quot = this.Current;
			bool isQuoted =
				(quot == SingleQuoteChar) ||
				(quot == DoubleQuoteChar);

			HtmlTag tag;
			if (isQuoted)
			{
				// consume open quote
				this.EmptyBuffer(1);

				// parse for common inline script
				tag = this.ParseComment("<%", "%>");

				while (!this.IsEOF)
				{
					ch = this.Current;

					if ((ch == quot) ||
						(ch == CloseTagChar) ||
						(ch == OpenTagChar))
					{
						break;
					}

					// add to attribute value
					this.Advance();
				}
			}
			else
			{
				// parse for common inline script
				tag = this.ParseComment("<%", "%>");

				while (!this.IsEOF)
				{
					ch = this.Current;

					if ((ch == CloseTagChar) ||
						(ch == OpenTagChar) ||
						(Char.IsWhiteSpace(ch)))
					{
						break;
					}

					// add to attribute value
					this.Advance();
				}
			}

			string value = this.FlushBuffer();
			if (isQuoted && this.Current == quot)
			{
				// consume close quote
				this.EmptyBuffer(1);
			}

			if (tag != null && this.htmlFilter.FilterTag(tag))
			{
				return tag.ToString();
			}

			return value;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="tag"></param>
		/// <param name="style"></param>
		private void ParseStyles(HtmlTag tag, string style)
		{
			string name, value;
			int start=0, i=0;

			while (i < style.Length)
			{
				name = value = String.Empty;

				// skip whitespace
				while (i < style.Length &&
					Char.IsWhiteSpace(style, i))
				{
					start = ++i;
				}

				// style name
				while (i < style.Length &&
					style[i] != StylePropChar)
				{
					i++;
				}

				// copy style name
				if (start < style.Length)
				{
					name = style.Substring(start, i-start);
				}

				// inc first
				start = ++i;

				// skip whitespace
				while (i < style.Length &&
					Char.IsWhiteSpace(style, i))
				{
					// inc first
					start = ++i;
				}

				// style value
				while (i < style.Length &&
					style[i] != StyleDelimChar)
				{
					// TODO: handle HTML entities (e.g. "&quot;")
					i++;
				}

				if (!String.IsNullOrEmpty(name) &&
					start < style.Length)
				{
					// copy style value
					value = style.Substring(start, i-start);

					// apply style to tag
					tag.Styles[name.ToLowerInvariant()] = value;
				}

				// inc first
				start = ++i;
			}
		}

		private void RenderTag(HtmlTag tag)
		{
			if (this.htmlWriter.WriteTag(tag, this.htmlFilter))
			{
				this.taxonomy |= tag.Taxonomy;
			}
		}

		private void RenderCloseTag(HtmlTag tag)
		{
			tag = tag.CreateCloseTag();

			if (tag != null)
			{
				this.RenderTag(tag);
			}
		}

		/// <summary>
		/// Reset state used for parsing
		/// </summary>
		/// <param name="fullReset">clears incremental state as well</param>
		/// <remarks>Does not SyncLock, call inside lock</remarks>
		private void Init(string html)
		{
			if (this.htmlFilter == null)
			{
				this.htmlFilter = new SafeHtmlFilter();
			}
			if (this.htmlWriter == null)
			{
				this.htmlWriter = new HtmlWriter();
			}

			// set up the source
			if (this.incrementalParsing && this.syncPoint >= 0)
			{
				// prepend remaining unparsed source
				html = this.source.Substring(this.syncPoint) + html;
			}
			this.source = (html == null) ? String.Empty : html;

			// reset indexes
			this.index = this.start = 0;

			if (!this.isInitialized)
			{
				// in incremental parse mode, continue as if same document
				this.textSize = 0;
				this.syncPoint = -1;
				this.openTags = new Stack<HtmlTag>(10);
				this.taxonomy = HtmlTaxonomy.None;
				this.isInitialized = true;
			}
		}

		/// <summary>
		/// Causes parsing to end preserving partial source
		/// </summary>
		private void ParseSyncPoint()
		{
			if (this.incrementalParsing && this.IsEOF)
			{
				throw new UnexpectedEofException();
			}
		}

		#endregion Parse Methods

		#region Methods

		/// <summary>
		/// Remove whitespace from the input source
		/// </summary>
		private void SkipWhiteSpace()
		{
			while (!this.IsEOF && Char.IsWhiteSpace(this.Current))
			{
				this.EmptyBuffer(1);
			}
		}

		/// <summary>
		/// Gets a previous char whether buffered or written out
		/// </summary>
		/// <returns></returns>
		private char PrevChar(int peek)
		{
			if (this.index-this.start >= peek)
			{
				// use buffered
				int pos = this.index-peek;
				if (pos < 0 || pos >= this.source.Length)
				{
					return NullChar;
				}
				return this.source[pos];
			}

			// check the previous output if possible
			IReversePeek revPeek = this.HtmlWriter as IReversePeek;
			return (revPeek == null) ? NullChar : revPeek.PrevChar(peek);
		}

		private char Peek(int peek)
		{
			if ((this.index + peek) >= this.source.Length)
			{
				return NullChar;
			}

			return this.source[this.index + peek];
		}

		private void WriteBuffer()
		{
			// do not callback on empty strings
			if (this.start < this.index)
			{
				string replacement;
				if (this.htmlFilter.FilterLiteral(this.source, this.start, this.index, out replacement))
				{
					// filter has altered the literal
					this.HtmlWriter.WriteLiteral(replacement);
				}
				else
				{
					// use the original literal
					this.HtmlWriter.WriteLiteral(this.source.Substring(this.start, this.index-this.start));
				}
			}
			this.start = this.index;
		}

		private void EmptyBuffer()
		{
			this.EmptyBuffer(0);
		}

		private void EmptyBuffer(int skipCount)
		{
			this.index += skipCount;
			this.start = this.index;
		}

		private string FlushBuffer()
		{
			return this.FlushBuffer(0);
		}

		private string FlushBuffer(int skipCount)
		{
			this.index += skipCount;
			string buffer = (this.start < this.index) ?
				this.source.Substring(this.start, this.index-this.start):
				String.Empty;
			this.start = this.index;
			return buffer;
		}

		private void Advance()
		{
			// move index ahead
			this.index++;
		}

		/// <summary>
		/// Keeps running tally of the plain text length
		/// </summary>
		private void IncTextCount()
		{
			// count toward total text length
			this.textSize++;
		}

		/// <summary>
		/// Encodes special characters into safe representation
		/// </summary>
		/// <param name="ch"></param>
		/// <returns></returns>
		public static string EncodeHtmlEntity(char ch)
		{
			return String.Format(
				(ch > (char)0xFF) ? "&#x{0:X4};" : "&#x{0:X2};",
				(int)ch);
		}

		/// <summary>
		/// Decodes HTML entities into special characters
		/// </summary>
		/// <param name="source"></param>
		/// <param name="index"></param>
		/// <param name="entity"></param>
		/// <returns>the number of character consumed</returns>
		public static int DecodeHtmlEntity(string source, int index, out char entity)
		{
			int Start = index;
			int End = source.Length-1;

			// consume '&'
			index++;

			if ((index < End) &&
				(source[index] == HtmlDistiller.EntityNumChar))
			{
				// entity is Unicode Code Point

				// consume '#'
				index++;

				bool isHex = false;
				if ((index < End) &&
					(Char.ToLowerInvariant(source[index]) == HtmlDistiller.EntityHexChar))
				{
					isHex = true;

					// consume 'x'
					index++;

				}

				int NumStart = index;
				while ((index < End) && (source[index] != ';'))
				{
					char ch = Char.ToUpperInvariant(source[index]);
					if (!Char.IsDigit(ch) &&
						(isHex && (ch < HtmlDistiller.HexStartChar || ch > HtmlDistiller.HexEndChar)))
					{
						break;
					}

					index++;
				}

				int codePoint;
				if (Int32.TryParse(
					source.Substring(NumStart, index-NumStart),
					isHex ? NumberStyles.AllowHexSpecifier : NumberStyles.None,
					CultureInfo.InvariantCulture,
					out codePoint))
				{
					entity = (char)codePoint;
					if (source[index] == ';')
					{
						index++;
					}
					return index-Start;
				}
				else
				{
					entity = EntityStartChar;
					return 1;
				}
			}

			int NameStart = index;
			while ((index < End) && (source[index] != ';'))
			{
				if (!Char.IsLetter(source, index))
				{
					break;
				}

				index++;
			}

			entity = HtmlDistiller.MapEntityName(source.Substring(NameStart, index-NameStart));
			if (entity == '\0')
			{
				entity = HtmlDistiller.EntityStartChar;
				return 1;
			}

			if (source[index] == ';')
			{
				index++;
			}
			return index-Start;
		}

		private static char MapEntityName(string name)
		{
			// http://www.w3.org/TR/REC-html40/sgml/entities.html
			// http://www.bigbaer.com/sidebars/entities/
			// NOTE: these names are case-sensitive
			switch (name)
			{
				case "quot": { return (char)34; }
				case "amp": { return (char)38; }
				case "lt": { return (char)60; }
				case "gt": { return (char)62; }
				case "nbsp": { return (char)160; }
				case "iexcl": { return (char)161; }
				case "cent": { return (char)162; }
				case "pound": { return (char)163; }
				case "curren": { return (char)164; }
				case "yen": { return (char)165; }
				case "euro": { return (char)8364; }
				case "brvbar": { return (char)166; }
				case "sect": { return (char)167; }
				case "uml": { return (char)168; }
				case "copy": { return (char)169; }
				case "ordf": { return (char)170; }
				case "laquo": { return (char)171; }
				case "not": { return (char)172; }
				case "shy": { return (char)173; }
				case "reg": { return (char)174; }
				case "trade": { return (char)8482; }
				case "macr": { return (char)175; }
				case "deg": { return (char)176; }
				case "plusmn": { return (char)177; }
				case "sup2": { return (char)178; }
				case "sup3": { return (char)179; }
				case "acute": { return (char)180; }
				case "micro": { return (char)181; }
				case "para": { return (char)182; }
				case "middot": { return (char)183; }
				case "cedil": { return (char)184; }
				case "sup1": { return (char)185; }
				case "ordm": { return (char)186; }
				case "raquo": { return (char)187; }
				case "frac14": { return (char)188; }
				case "frac12": { return (char)189; }
				case "frac34": { return (char)190; }
				case "iquest": { return (char)191; }
				case "times": { return (char)215; }
				case "divide": { return (char)247; }
				case "Agrave": { return (char)192; }
				case "Aacute": { return (char)193; }
				case "Acirc": { return (char)194; }
				case "Atilde": { return (char)195; }
				case "Auml": { return (char)196; }
				case "Aring": { return (char)197; }
				case "AElig": { return (char)198; }
				case "Ccedil": { return (char)199; }
				case "Egrave": { return (char)200; }
				case "Eacute": { return (char)201; }
				case "Ecirc": { return (char)202; }
				case "Euml": { return (char)203; }
				case "Igrave": { return (char)204; }
				case "Iacute": { return (char)205; }
				case "Icirc": { return (char)206; }
				case "Iuml": { return (char)207; }
				case "ETH": { return (char)208; }
				case "Ntilde": { return (char)209; }
				case "Ograve": { return (char)210; }
				case "Oacute": { return (char)211; }
				case "Ocirc": { return (char)212; }
				case "Otilde": { return (char)213; }
				case "Ouml": { return (char)214; }
				case "Oslash": { return (char)216; }
				case "Ugrave": { return (char)217; }
				case "Uacute": { return (char)218; }
				case "Ucirc": { return (char)219; }
				case "Uuml": { return (char)220; }
				case "Yacute": { return (char)221; }
				case "THORN": { return (char)222; }
				case "szlig": { return (char)223; }
				case "agrave": { return (char)224; }
				case "aacute": { return (char)225; }
				case "acirc": { return (char)226; }
				case "atilde": { return (char)227; }
				case "auml": { return (char)228; }
				case "aring": { return (char)229; }
				case "aelig": { return (char)230; }
				case "ccedil": { return (char)231; }
				case "egrave": { return (char)232; }
				case "eacute": { return (char)233; }
				case "ecirc": { return (char)234; }
				case "euml": { return (char)235; }
				case "igrave": { return (char)236; }
				case "iacute": { return (char)237; }
				case "icirc": { return (char)238; }
				case "iuml": { return (char)239; }
				case "eth": { return (char)240; }
				case "ntilde": { return (char)241; }
				case "ograve": { return (char)242; }
				case "oacute": { return (char)243; }
				case "ocirc": { return (char)244; }
				case "otilde": { return (char)245; }
				case "ouml": { return (char)246; }
				case "oslash": { return (char)248; }
				case "ugrave": { return (char)249; }
				case "uacute": { return (char)250; }
				case "ucirc": { return (char)251; }
				case "uuml": { return (char)252; }
				case "yacute": { return (char)253; }
				case "thorn": { return (char)254; }
				case "yuml": { return (char)255; }
				case "OElig": { return (char)338; }
				case "oelig": { return (char)339; }
				case "Scaron": { return (char)352; }
				case "scaron": { return (char)353; }
				case "Yuml": { return (char)376; }
				case "circ": { return (char)710; }
				case "tilde": { return (char)732; }
				case "ensp": { return (char)8194; }
				case "emsp": { return (char)8195; }
				case "thinsp": { return (char)8201; }
				case "zwnj": { return (char)8204; }
				case "zwj": { return (char)8205; }
				case "lrm": { return (char)8206; }
				case "rlm": { return (char)8207; }
				case "ndash": { return (char)8211; }
				case "mdash": { return (char)8212; }
				case "lsquo": { return (char)8216; }
				case "rsquo": { return (char)8217; }
				case "sbquo": { return (char)8218; }
				case "ldquo": { return (char)8220; }
				case "rdquo": { return (char)8221; }
				case "bdquo": { return (char)8222; }
				case "dagger": { return (char)8224; }
				case "Dagger": { return (char)8225; }
				case "permil": { return (char)8240; }
				case "lsaquo": { return (char)8249; }
				case "rsaquo": { return (char)8250; }
				case "fnof": { return (char)402; }
				case "bull": { return (char)8226; }
				case "hellip": { return (char)8230; }
				case "prime": { return (char)8242; }
				case "Prime": { return (char)8243; }
				case "oline": { return (char)8254; }
				case "frasl": { return (char)8260; }
				case "weierp": { return (char)8472; }
				case "image": { return (char)8465; }
				case "real": { return (char)8476; }
				case "alefsym": { return (char)8501; }
				case "larr": { return (char)8592; }
				case "uarr": { return (char)8593; }
				case "rarr": { return (char)8594; }
				case "darr": { return (char)8495; }
				case "harr": { return (char)8596; }
				case "crarr": { return (char)8629; }
				case "lArr": { return (char)8656; }
				case "uArr": { return (char)8657; }
				case "rArr": { return (char)8658; }
				case "dArr": { return (char)8659; }
				case "hArr": { return (char)8660; }
				case "forall": { return (char)8704; }
				case "part": { return (char)8706; }
				case "exist": { return (char)8707; }
				case "empty": { return (char)8709; }
				case "nabla": { return (char)8711; }
				case "isin": { return (char)8712; }
				case "notin": { return (char)8713; }
				case "ni": { return (char)8715; }
				case "prod": { return (char)8719; }
				case "sum": { return (char)8721; }
				case "minus": { return (char)8722; }
				case "lowast": { return (char)8727; }
				case "radic": { return (char)8730; }
				case "prop": { return (char)8733; }
				case "infin": { return (char)8734; }
				case "ang": { return (char)8736; }
				case "and": { return (char)8743; }
				case "or": { return (char)8744; }
				case "cap": { return (char)8745; }
				case "cup": { return (char)8746; }
				case "int": { return (char)8747; }
				case "there4": { return (char)8756; }
				case "sim": { return (char)8764; }
				case "cong": { return (char)8773; }
				case "asymp": { return (char)8776; }
				case "ne": { return (char)8800; }
				case "equiv": { return (char)8801; }
				case "le": { return (char)8804; }
				case "ge": { return (char)8805; }
				case "sub": { return (char)8834; }
				case "sup": { return (char)8835; }
				case "nsub": { return (char)8836; }
				case "sube": { return (char)8838; }
				case "supe": { return (char)8839; }
				case "oplus": { return (char)8853; }
				case "otimes": { return (char)8855; }
				case "perp": { return (char)8869; }
				case "sdot": { return (char)8901; }
				case "lceil": { return (char)8968; }
				case "rceil": { return (char)8969; }
				case "lfloor": { return (char)8970; }
				case "rfloor": { return (char)8971; }
				case "lang": { return (char)9001; }
				case "rang": { return (char)9002; }
				case "loz": { return (char)9674; }
				case "spades": { return (char)9824; }
				case "clubs": { return (char)9827; }
				case "hearts": { return (char)9829; }
				case "diams": { return (char)9830; }
				case "Alpha": { return (char)913; }
				case "Beta": { return (char)914; }
				case "Gamma": { return (char)915; }
				case "Delta": { return (char)916; }
				case "Epsilon": { return (char)917; }
				case "Zeta": { return (char)918; }
				case "Eta": { return (char)919; }
				case "Theta": { return (char)920; }
				case "Iota": { return (char)921; }
				case "Kappa": { return (char)922; }
				case "Lambda": { return (char)923; }
				case "Mu": { return (char)924; }
				case "Nu": { return (char)925; }
				case "Xi": { return (char)926; }
				case "Omicron": { return (char)927; }
				case "Pi": { return (char)928; }
				case "Rho": { return (char)929; }
				case "Sigma": { return (char)931; }
				case "Tau": { return (char)932; }
				case "Upsilon": { return (char)933; }
				case "Phi": { return (char)934; }
				case "Chi": { return (char)935; }
				case "Psi": { return (char)936; }
				case "Omega": { return (char)937; }
				case "alpha": { return (char)945; }
				case "beta": { return (char)946; }
				case "gamma": { return (char)947; }
				case "delta": { return (char)948; }
				case "epsilon": { return (char)949; }
				case "zeta": { return (char)950; }
				case "eta": { return (char)951; }
				case "theta": { return (char)952; }
				case "iota": { return (char)953; }
				case "kappa": { return (char)954; }
				case "lambda": { return (char)955; }
				case "mu": { return (char)956; }
				case "nu": { return (char)957; }
				case "xi": { return (char)958; }
				case "omicron": { return (char)959; }
				case "pi": { return (char)960; }
				case "rho": { return (char)961; }
				case "sigmaf": { return (char)962; }
				case "sigma": { return (char)963; }
				case "tau": { return (char)964; }
				case "upsilon": { return (char)965; }
				case "phi": { return (char)966; }
				case "chi": { return (char)967; }
				case "psi": { return (char)968; }
				case "omega": { return (char)969; }
				case "thetasym": { return (char)977; }
				case "upsih": { return (char)978; }
				case "piv": { return (char)982; }
				default:
				{
					return '\0';
				}
			}
		}

		#endregion Methods

		#region Static Methods

		/// <summary>
		/// Quick parsing utility for common usage.
		/// </summary>
		/// <param name="html">the source text</param>
		/// <param name="filter">a custom HtmlFilter</param>
		/// <returns>the filtered text</returns>
		public static string Parse(string html, IHtmlFilter filter)
		{
			return HtmlDistiller.Parse(html, filter);
		}

		/// <summary>
		/// Quick parsing utility for common usage.
		/// </summary>
		/// <param name="html">the source text</param>
		/// <param name="filter">a custom HtmlFilter</param>
		/// <param name="maxLength">the maximum text length</param>
		/// <returns>the filtered text</returns>
		public static string Parse(string html, IHtmlFilter filter, int maxLength)
		{
			StringWriter writer = new StringWriter();

			HtmlDistiller parser = new HtmlDistiller(maxLength, filter);
			parser.HtmlWriter = new HtmlWriter(writer);
			parser.Parse(html);

			return writer.ToString();
		}

		#endregion Static Methods
	}
}
