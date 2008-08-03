#region BuildTools License
/*---------------------------------------------------------------------------------*\

	BuildTools distributed under the terms of an MIT-style license:

	The MIT License

	Copyright (c) 2006-2007 Stephen M. McKamey

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
using System.Net;

using BuildTools.IO;
using BuildTools.HtmlDistiller.Filters;
using BuildTools.HtmlDistiller.Writers;

namespace BuildTools.HtmlDistiller
{
	class Program
	{
		#region Program Entry

		static void Main(string[] args)
		{
			string template = File.ReadAllText(@"D:\Dev\SMM-VSS\PseudoCode\Dev\BuildTools\HtmlDistiller.Spider\ItemTemplate.jbst");
			HtmlDistiller parser = new HtmlDistiller(0, new UnsafeHtmlFilter());
			parser.EncodeNonAscii = false;

			using (HtmlWriter writer = new HtmlWriter(File.OpenWrite(@"D:\Dev\SMM-VSS\PseudoCode\Dev\BuildTools\HtmlDistiller.Spider\Output.jbst")))
			{
				parser.HtmlWriter = writer;
				parser.Parse(template);
			}
			string output = File.ReadAllText(@"D:\Dev\SMM-VSS\PseudoCode\Dev\BuildTools\HtmlDistiller.Spider\Output.jbst");
			return;

			try
			{
				Console.Write("Enter start URL: ");
				string startUrl = Console.ReadLine();
				using (ExampleSpider spider = new ExampleSpider(startUrl, true))
				{
					spider.Crawl(null);
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine(ex.ToString());
			}
			Console.WriteLine("Done.");
			Console.ReadLine();
		}

		#endregion Program Entry
	}
}
