using System;
using System.Collections;

using JsonFx.BuildTools.HtmlDistiller.Writers;

namespace JsonFx.BuildTools.HtmlDistiller
{
	class Program
	{
		static void Main(string[] args)
		{
			// basic example showing generating markup using HtmlDistiller

			ArrayList list = new ArrayList();
			HtmlWriter writer = new HtmlWriter();

			HtmlTag root = new HtmlTag("div");
			root.Attributes["class"] = "content";
			root.Styles["color"] = "red";

			list.Add(root); //<div class="content" style="color:red">
			list.Add("Lorem ipsum"); // Lorem ipsum
			list.Add(new HtmlTag("hr")); //<hr />
			list.Add("hello world."); // hello world.
			list.Add(root.CreateCloseTag()); //</div>

			writer.WriteMarkup(list);// <div class="content" style="color:red;">Lorem ipsum<hr />hello world.</div>

			Console.WriteLine(writer.ToString());
		}
	}
}
