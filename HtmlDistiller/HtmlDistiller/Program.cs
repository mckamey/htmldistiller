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
			"\tHtmlDistiller.exe fileOut fileIn";

		#endregion Constants

		#region Program Entry

		static void Main(string[] args)
		{
			// less than 2 files doesn't make sense
			if (args.Length < 2)
			{
				Console.Error.WriteLine(Help);
				Environment.Exit(1);
			}

			string outputFile = args[0];
			string inputFile = args[1];

			// check the input file before start
			if (!File.Exists(inputFile))
			{
				Console.Error.WriteLine("File does not exist: "+inputFile);
				Environment.Exit(2);
			}

			// make sure path exists and destination is not readonly
			FileUtility.PrepSavePath(outputFile);

			string source = File.ReadAllText(inputFile);
			HtmlDistiller distiller = new HtmlDistiller(source, new ExampleHtmlFilter(48));
			string output = distiller.Parse();

			File.WriteAllText(outputFile, output, System.Text.Encoding.UTF8);
		}

		#endregion Program Entry
	}
}
