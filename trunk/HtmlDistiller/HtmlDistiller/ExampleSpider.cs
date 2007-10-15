using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Net;

using BuildTools.IO;
using BuildTools.Collections;
using BuildTools.HtmlDistiller.Filters;

namespace BuildTools.HtmlDistiller
{
	public class ExampleSpider : SafeHtmlFilter, IDisposable
	{
		#region Constants

		private const string QueueFile = "_Queue.txt";
		private const string DefaultFile = "index.html";
		private const string PathFormat = @"\{0}";
		private static readonly char[] HostSplit = new char[] { '.' };
		private static readonly char[] PathSplit = new char[] { '/' };

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

		public ExampleSpider(string startUrl, bool onlyWithinDomain) : base(20)
		{
			this.Parser.HtmlFilter = this;
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

					try
					{
						if (!FileUtility.PrepSavePath(path))
						{
							continue;
						}
						if (File.Exists(path))
						{
							File.Delete(path);
						}
					}
					catch (IOException ex)
					{
						Console.Error.WriteLine(ex.Message+" ("+this.currentUri+")");
						continue;
					}

					Console.WriteLine(this.currentUri.AbsoluteUri);

					// TODO: use HTTP HEAD to get the Content-Type?
					this.Browser.DownloadFile(this.currentUri, path);
					string contentType = this.Browser.ResponseHeaders[HttpResponseHeader.ContentType];
					if (contentType != null &&
						contentType.IndexOf("html", StringComparison.InvariantCultureIgnoreCase) >= 0)
					{
						this.Parser.Source = File.ReadAllText(path);
						this.Parser.Parse();
					}
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

			if (!uri.IsFile)
			{
				builder.AppendFormat(PathFormat, DefaultFile);
			}
			//TODO: add query string encoding into path/filename

			return builder.Replace(':', '_').ToString();
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
				this.QueueWriter = new StreamWriter(enqueueStream, Encoding.ASCII);
			}

			this.QueueWriter.WriteLine(url);
			this.QueueWriter.Flush();
		}

		private string Dequeue()
		{
			if (this.QueueReader == null)
			{
				FileStream dequeueStream = new FileStream(ExampleSpider.QueueFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				this.QueueReader = new StreamReader(dequeueStream, Encoding.ASCII);
			}

			if (this.QueueReader.EndOfStream)
			{
				return null;
			}

			return this.QueueReader.ReadLine();
		}

		#endregion Methods

		#region IHtmlFilter Members

		public override bool FilterTag(HtmlTag tag)
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
							url = tag.Attributes["href"];
						}
						break;
					}
					case "iframe":
					case "frame":
					{
						if (tag.Attributes.ContainsKey("src"))
						{
							url = tag.Attributes["src"];
						}
						break;
					}
				}

				Uri uri;
				if (!String.IsNullOrEmpty(url) && Uri.TryCreate(this.currentUri, url, out uri))
				{
					// should put scripts, css and images into another bucket or label by type?
					this.Enqueue(uri.AbsoluteUri);
				}

			}
			return false;
		}

		#endregion

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

		#endregion
	}
}
