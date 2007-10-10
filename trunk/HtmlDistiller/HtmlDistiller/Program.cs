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

namespace BuildTools.HtmlDistiller
{
	class Program
	{
		#region Constants

		private const string Help =
			"Processes HTML using various filter levels.\r\n\r\n"+
			"Usage:\r\n"+
			"\tHtmlDistiller.exe fileIn fileOut";

		#endregion Constants

		#region Program Entry

		static void Main(string[] args)
		{
			// less than 1 path show help
			if (args.Length < 1)
			{
				Console.Error.WriteLine(Help);
				Environment.Exit(1);
			}

			string source;
			string inputFile = args[0];
			string outputFile = args.Length > 1 ? args[1] : "Output.html";

			if (inputFile != null && inputFile.StartsWith("http://"))
			{
				WebClient client = new WebClient();
				try
				{
					source = client.DownloadString(inputFile);
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine(ex.Message);
					Environment.Exit(2);
					return;
				}
			}
			else
			{
				// check the input file before start
				if (!File.Exists(inputFile))
				{
					Console.Error.WriteLine("File does not exist: "+inputFile);
					Environment.Exit(2);
				}
				source = File.ReadAllText(inputFile);
			}

			// make sure path exists and destination is not readonly
			FileUtility.PrepSavePath(outputFile);

			HtmlDistiller distiller = new HtmlDistiller();
			distiller.Source = source;
			distiller.MaxLength = 20480;
			distiller.HtmlFilter = new ExampleHtmlFilter(96);
			distiller.NormalizeWhitespace = true;
			string output = distiller.Output;
			HtmlTaxonomy moduleTypes = distiller.Taxonomy;

			File.WriteAllText(outputFile, output, System.Text.Encoding.UTF8);
		}

		#endregion Program Entry
	}
}
