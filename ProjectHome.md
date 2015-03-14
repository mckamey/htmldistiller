# HtmlDistiller #
A **light-weight markup parser** which allows filtering and munging of HTML. **Does not require the source to be XHTML-compliant.**

Features pluggable sets of custom HTML filters. Can filter to any arbitrary set of tag / attribute / style. Easy to implement **white-lists or black-lists**.

Optional limit for total length of literal-text (i.e. not counting tags, HTML entities).

Optionally **encodes** non-ASCII characters, or **optionally decodes**HTML entities**.**

Optional **whitespace normalization**.

Includes an example app which is a **simple web crawler**.