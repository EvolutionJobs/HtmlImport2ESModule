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
