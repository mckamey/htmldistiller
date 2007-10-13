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
	public class ExampleSpider : SafeHtmlFilter
	{
		#region Constants

		private const string DefaultFile = "index.html";
		private const string PathFormat = @"\{0}";
		private static readonly char[] HostSplit = new char[] { '.' };
		private static readonly char[] PathSplit = new char[] { '/' };

		#endregion Constants

		#region Fields

		private readonly StringDictionary<bool> Cache = new StringDictionary<bool>(false);
		private readonly HtmlDistiller Parser = new HtmlDistiller();
		private readonly WebClient Browser = new WebClient();
		private readonly Queue<string> Queue = new Queue<string>(50);
		private Uri currentUri = null;

		#endregion Fields

		#region Init

		public ExampleSpider(string startUri) : base(20)
		{
			this.Parser.HtmlFilter = this;
			this.Parser.NormalizeWhitespace = false;
			this.Queue.Enqueue(startUri);
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

			while (this.Queue.Count > 0)
			{
				string path = null, url = null;
				try
				{
					url = this.Queue.Dequeue();
					if (!Uri.TryCreate(url, UriKind.Absolute, out this.currentUri))
					{
						continue;
					}

					if (!this.currentUri.Scheme.StartsWith("http", StringComparison.InvariantCultureIgnoreCase))
					{
						continue;
					}

					path = this.GetUniquePath(this.currentUri, savePath);
					if (this.Cache.ContainsKey(this.currentUri.AbsoluteUri))
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
					Console.Error.WriteLine(ex.Message+" ("+this.currentUri+")");
				}
				catch (UriFormatException ex)
				{
					Console.Error.WriteLine(ex.Message+" ("+url+")");
				}
				catch (Exception ex)
				{
					try
					{
						string error = this.currentUri+Environment.NewLine+ex+Environment.NewLine+Environment.NewLine;
						File.AppendAllText("_Errors.txt", error, Encoding.UTF8);
					}
					catch
					{
						Console.Error.WriteLine(ex);
					}
					continue;
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

		#endregion Methods

		#region IHtmlFilter Members

		public override bool FilterTag(HtmlTag tag)
		{
			if (tag.Taxonomy == HtmlTaxonomy.Unknown)
			{
				File.AppendAllText("_UnknownTags.txt", tag+Environment.NewLine);
			}

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
					this.Queue.Enqueue(uri.AbsoluteUri);
				}

			}
			return false;
		}

		#endregion
	}
}
