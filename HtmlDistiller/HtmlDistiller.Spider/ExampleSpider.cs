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
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Net;

using JsonFx.BuildTools.IO;
using JsonFx.BuildTools.Collections;
using JsonFx.BuildTools.HtmlDistiller.Filters;
using JsonFx.BuildTools.HtmlDistiller.Writers;

namespace JsonFx.BuildTools.HtmlDistiller
{
	public class ExampleSpider :
		IHtmlFilter,
		IDisposable
	{
		#region Constants

		private const string UserAgent = "HtmlDistiller/1.0.0908.1515";
		private const string QueueFile = "_Queue.txt";
		private const string DefaultFile = "index.html";
		private const string PathFormat = @"\{0}";
		private static readonly char[] HostSplit = new char[] { '.', ':' };
		private static readonly char[] PathSplit = new char[] { '/', ':' };

		#endregion Constants

		#region Fields

		private readonly StringDictionary<bool> Cache = new StringDictionary<bool>(false);
		private readonly HtmlDistiller Parser = new HtmlDistiller();
		private readonly WebClient Browser = new WebClient();
		private Uri currentUri = null;
		private StreamReader QueueReader = null;
		private StreamWriter QueueWriter = null;
		private string domainBound = null;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="startUrl"></param>
		/// <param name="onlyWithinDomain"></param>
		public ExampleSpider(string startUrl, bool onlyWithinDomain)
		{
			this.Browser.Headers[HttpRequestHeader.UserAgent] = ExampleSpider.UserAgent;

			this.Parser.HtmlFilter = this;
			this.Parser.HtmlWriter = new HtmlWriter(StreamWriter.Null);
			this.Parser.NormalizeWhitespace = true;

			if (Uri.TryCreate(startUrl, UriKind.Absolute, out this.currentUri))
			{
				if (onlyWithinDomain)
				{
					this.domainBound = this.currentUri.DnsSafeHost;
				}
				this.Enqueue(startUrl);
			}
		}

		#endregion Init

		#region Methods

		/// <summary>
		/// Initiates a recursive walk of the web.
		/// </summary>
		/// <param name="savePath"></param>
		public void Crawl(string savePath)
		{
			if (String.IsNullOrEmpty(savePath))
			{
				savePath = @".\_WebCache";
			}
			else if (savePath[savePath.Length-1] == Path.DirectorySeparatorChar)
			{
				savePath = savePath.TrimEnd(Path.DirectorySeparatorChar);
			}

			string url = null;
			while (!String.IsNullOrEmpty(url = this.Dequeue()))
			{
				string path = null;
				try
				{
					if (!Uri.TryCreate(url, UriKind.Absolute, out this.currentUri))
					{
						continue;
					}

					if (this.domainBound != null &&
						!this.domainBound.Equals(this.currentUri.DnsSafeHost, StringComparison.InvariantCultureIgnoreCase))
					{
						// stay within domain
						continue;
					}

					path = this.GetUniquePath(this.currentUri, savePath);
					if (this.Cache.ContainsKey(this.currentUri.AbsoluteUri))//File.Exists(path))
					{
						continue;
					}
					this.Cache[this.currentUri.AbsoluteUri] = true;

					if (!FileUtility.PrepSavePath(path))
					{
						continue;
					}
					if (File.Exists(path))
					{
						File.Delete(path);
					}

					// TODO: use HTTP HEAD to determine the Content-Type?
					this.Browser.Headers[HttpRequestHeader.Referer] = this.currentUri.ToString();
					this.Browser.DownloadFile(this.currentUri, path);

					string contentType = this.Browser.ResponseHeaders[HttpResponseHeader.ContentType];
					if (contentType != null &&
						contentType.IndexOf("html", StringComparison.InvariantCultureIgnoreCase) >= 0)
					{
						Console.WriteLine(this.currentUri.AbsoluteUri);
						string source = File.ReadAllText(path);
						this.Parser.Parse(source);
					}
				}
				catch (IOException ex)
				{
					File.AppendAllText("_IOErrors.txt", ex.Message+"\t"+this.currentUri+Environment.NewLine, Encoding.UTF8);
				}
				catch (WebException ex)
				{
					File.AppendAllText("_WebErrors.txt", ex.Message+"\t"+this.currentUri+Environment.NewLine, Encoding.UTF8);
				}
				catch (UriFormatException ex)
				{
					File.AppendAllText("_UrlErrors.txt", ex.Message+"\t"+this.currentUri+Environment.NewLine, Encoding.UTF8);
				}
				catch (Exception ex)
				{
					string error = this.currentUri+Environment.NewLine+ex+Environment.NewLine+Environment.NewLine;
					File.AppendAllText("_Errors.txt", error, Encoding.UTF8);
				}
			}
		}

		/// <summary>
		/// Generates a unique path which clusters similar files near each other
		/// </summary>
		/// <param name="uri"></param>
		/// <param name="root"></param>
		/// <returns></returns>
		private string GetUniquePath(Uri uri, string root)
		{
			if (!uri.IsAbsoluteUri)
			{
				throw new ArgumentException("Must be an absolute Uri.", "uri");
			}

			StringBuilder builder = new StringBuilder(root, uri.AbsoluteUri.Length);
			string[] parts = uri.Host.Split(HostSplit, StringSplitOptions.RemoveEmptyEntries);
			for (int i=parts.Length-1; i>=0; i--)
			{
				builder.AppendFormat(PathFormat, parts[i]);
			}

			parts = uri.AbsolutePath.Split(PathSplit, StringSplitOptions.RemoveEmptyEntries);
			for (int i=0; i<parts.Length; i++)
			{
				builder.AppendFormat(PathFormat, parts[i]);
			}

			if (!String.IsNullOrEmpty(uri.Query))
			{
				// don't care so much about query strings as open to interpretation
				// but this will allow multiple to same document
				builder.AppendFormat(PathFormat, "_QueryHash="+uri.Query.GetHashCode());
			}

			string temp = builder.ToString();
			if (temp.LastIndexOf('.') < temp.LastIndexOf(Path.DirectorySeparatorChar))
			{
				builder.AppendFormat(PathFormat, DefaultFile);
				temp = builder.ToString();
			}

			return temp;
		}

		private void Enqueue(string url)
		{
			if (url == null || !url.StartsWith("http", StringComparison.InvariantCultureIgnoreCase))
			{
				return;
			}

			if (this.QueueWriter == null)
			{
				FileStream enqueueStream = new FileStream(ExampleSpider.QueueFile, FileMode.Create, FileAccess.Write, FileShare.Read);
				this.QueueWriter = new StreamWriter(enqueueStream, Encoding.UTF8);
			}

			this.QueueWriter.WriteLine(url);
			this.QueueWriter.Flush();
		}

		private string Dequeue()
		{
			if (this.QueueReader == null)
			{
				FileStream dequeueStream = new FileStream(ExampleSpider.QueueFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				this.QueueReader = new StreamReader(dequeueStream, Encoding.UTF8);
			}

			if (this.QueueReader.EndOfStream)
			{
				return null;
			}

			return this.QueueReader.ReadLine();
		}

		#endregion Methods

		#region IHtmlFilter Members

		IHtmlWriter IHtmlFilter.HtmlWriter
		{
			get { return null; }
			set { }
		}

		/// <summary>
		/// Gaining access to very specific properties by using an IHtmlFilter interface
		/// </summary>
		/// <param name="tag"></param>
		/// <returns></returns>
		bool IHtmlFilter.FilterTag(HtmlTag tag)
		{
			if (tag.HasAttributes)
			{
				string url = null;
				switch (tag.TagName)
				{
					case "a":
					case "link":
					{
						if (tag.Attributes.ContainsKey("href"))
						{
							url = tag.Attributes["href"] as string;
						}
						break;
					}
					case "iframe":
					case "frame":
					{
						if (tag.Attributes.ContainsKey("src"))
						{
							url = tag.Attributes["src"] as string;
						}
						break;
					}
				}

				Uri uri;
				if (!String.IsNullOrEmpty(url) && Uri.TryCreate(this.currentUri, url, out uri))
				{
					// TODO: put scripts, css and images into another bucket or label by type?
					this.Enqueue(uri.AbsoluteUri);
				}

			}
			return false;
		}

		bool IHtmlFilter.FilterAttribute(string tag, string attribute, ref string value)
		{
			return false;
		}

		bool IHtmlFilter.FilterStyle(string tag, string style, ref string value)
		{
			return false;
		}

		bool IHtmlFilter.FilterLiteral(string source, int start, int end, out string replacement)
		{
			replacement = null;
			return true;
		}

		#endregion IHtmlFilter Members

		#region IDisposable Members

		void IDisposable.Dispose()
		{
			if (this.QueueWriter != null)
			{
				this.QueueWriter.Dispose();
				this.QueueWriter = null;
			}
			if (this.QueueReader != null)
			{
				this.QueueReader.Dispose();
				this.QueueReader = null;
			}
		}

		#endregion IDisposable Members
	}
}
