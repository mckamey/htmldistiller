using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Net;

using BuildTools.IO;
using BuildTools.HtmlDistiller.Filters;

namespace BuildTools.HtmlDistiller
{
	public class ExampleSpider : IHtmlFilter
	{
		#region Constants

		private const string DefaultFile = "index.html";
		private const string PathFormat = @"\{0}";
		private static readonly char[] HostSplit = new char[] { '.' };
		private static readonly char[] PathSplit = new char[] { '/' };

		#endregion Constants

		#region Fields

		private readonly Dictionary<string, string> cache = new Dictionary<string, string>(100, StringComparer.InvariantCultureIgnoreCase);
		private readonly HtmlDistiller parser = new HtmlDistiller();
		private readonly WebClient browser = new WebClient();
		private readonly Queue<string> queue = new Queue<string>(50);
		private Uri currentUri = null;

		#endregion Fields

		#region Init

		public ExampleSpider(string startUri)
		{
			this.parser.HtmlFilter = this;
			this.parser.NormalizeWhitespace = false;
			this.queue.Enqueue(startUri);
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

			while (this.queue.Count > 0)
			{
				string url = this.queue.Dequeue();
				if (!Uri.TryCreate(url, UriKind.Absolute, out this.currentUri))
				{
					continue;
				}
				string path = this.GetUniquePath(this.currentUri, savePath);
				FileUtility.PrepSavePath(path);

				if (File.Exists(path) || this.cache.ContainsKey(this.currentUri.AbsoluteUri))
				{
					continue;
				}

				this.cache[this.currentUri.AbsoluteUri] = path;

				try
				{
					Console.WriteLine(this.currentUri.AbsoluteUri);

					// TODO: use HTTP HEAD to get the Content-Type?
					this.parser.Source = this.browser.DownloadString(this.currentUri);
					string contentType = this.browser.ResponseHeaders[HttpResponseHeader.ContentType];
					if (contentType == null ||
						contentType.IndexOf("html", StringComparison.InvariantCultureIgnoreCase) < 0)
					{
						//this.browser.DownloadFile(this.currentUri, path);
						continue;
					}
					else
					{
						File.WriteAllText(path, this.parser.Output, Encoding.UTF8);
					}
				}
				catch (Exception ex)
				{
					try
					{
						File.WriteAllText(path, ex.ToString(), Encoding.UTF8);
					}
					catch
					{
						Console.Error.WriteLine(ex.Message);
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

			return builder.ToString();
		}

		#endregion Methods

		#region IHtmlFilter Members

		bool IHtmlFilter.FilterTag(HtmlTag tag)
		{
			if (tag.HasAttributes)
			{
				string url = null;
				switch (tag.TagName)
				{
					case "a":
					{
						url = tag.Attributes["href"];
						break;
					}
					case "iframe":
					case "frame":
					{
						url = tag.Attributes["src"];
						break;
					}
				}

				Uri uri;
				if (!String.IsNullOrEmpty(url) && Uri.TryCreate(this.currentUri, url, out uri))
				{
					// should put scripts, css and images into another bucket or label by type?
					this.queue.Enqueue(uri.AbsoluteUri);
				}

			}
			return true;
		}

		bool IHtmlFilter.FilterAttribute(string tag, string attribute, ref string value)
		{
			return true;
		}

		bool IHtmlFilter.FilterStyle(string tag, string style, ref string value)
		{
			return true;
		}

		#endregion
	}
}
