# HTML Import to ES Module
Regex file parser to find and insert HTML imports into JS files.

This checks for JS files with matching HTML (for the code behind pattern) then just looks for HTML files.

Then this:

- Tries to parse any JS from `<script>` tags in the HTML.
- Looks for the `static get is()` property Polymer 2 uses to find the `<dom-module>`.
- Finds the `<template>` in the relevant `<dom-module>`. 
- Extracts any scripts nested inside the template.
- Extracts any `<style>` content into a `const`
- CSS includes are included as explicit variables - **the files themselves need to be converted manually**.
- HTML `import` directives are converted to ES Modules.
- `Polymer.Element` is replaced by `PolymerElement`.
- HTML template inserted as a literal string in the JS file.
- JS file writtent to disk.
- HTML file deleted.

## Caveats

This is very much utlity code, with most corner cases just crashing. Expect bugs.

Dependencies are assumed to be in a library directory, and the name of this directory is used to find/replace dependencies references.
This is a very quick and dirty way to do this - if you want to use this in your own application you will either have to use the same pattern or write the relative link checking code yourself.

## Why not use the offical [polymer-modulizer](https://github.com/Polymer/polymer-modulizer)?

Because it doesn't work. 
Specifically:

- [polymer-modulizer#428](https://github.com/Polymer/polymer-modulizer/issues/428) It runs out of memory.
- [polymer-modulizer#429](https://github.com/Polymer/polymer-modulizer/issues/429) It breaks if updating isolated from Git.
- [polymer-modulizer#438](https://github.com/Polymer/polymer-modulizer/issues/438) It builds `<div>` tags with `innerText` rather than creating templates.

This doesn't aim to do all the complex parsing of JS that the modualizer aspires to - I just want to make the same pattern changes to a few hundred HTML import components.
