using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace HtmlImport2ESModule
{
    class Program
    {
        static readonly Regex HtmlImportPattern = new Regex(@"<link.*\s+rel=(""import""|import)\s+href=(?<href>[^>]+)>", RegexOptions.IgnoreCase | RegexOptions.ECMAScript | RegexOptions.Multiline);

        static readonly Regex DomModuleIsPattern = new Regex(@"static\s+get\s+is\s*\(\s*\)\s*\{\s*return\s*['|""](?<is>\w+(?>\-\w+)+)['|""]\s*\;*\s*}", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        static readonly Regex NestedScriptPattern = new Regex(@"\<script\s+.*src=""(?<src>[\w\-\.\d]+)"".*>\s*<\/script>", RegexOptions.IgnoreCase | RegexOptions.ECMAScript);

        static readonly Regex DomModuleTemplatePattern = new Regex(@"\<dom\-module\s+(?>strip-whitespace\s+)?id=\""(?<id>[\w\-]+)\""(?>\s+strip-whitespace)?\s*\>\s*<template(?>\s+strip-whitespace)?>\s*(?<template>.*)\s*<\/template>\s*<\/dom-module>", RegexOptions.IgnoreCase | RegexOptions.Singleline);


        static void WriteColour(string message, ConsoleColor background, ConsoleColor foreground) {
            var bg = Console.BackgroundColor;
            var fg = Console.ForegroundColor;
            Console.BackgroundColor = background;
            Console.ForegroundColor = foreground;
            Console.WriteLine(message);
            Console.BackgroundColor = bg;
            Console.ForegroundColor = fg;
        }

        static void WriteWarn(string message) => WriteColour(message, ConsoleColor.DarkYellow, ConsoleColor.Black);

        static void WriteError(string message) => WriteColour(message, ConsoleColor.DarkRed, ConsoleColor.White);

        static void WriteSuccess(string message) => WriteColour(message, ConsoleColor.DarkGreen, ConsoleColor.White);

        static void Main(string[] args)
        {
            Console.WriteLine("Running HTML Imports to ES Modules converter...");

            string target = null;
            if (args?.Length > 0)
                target = args[0];

            while (string.IsNullOrEmpty(target) ||
                !Directory.Exists(target))
            {
                Console.WriteLine("Specify a valid target directory:");
                target = Console.ReadLine();
            }

            Console.WriteLine($"Parsing files in {target}");

            var files = Directory.GetFileSystemEntries(target, "*.js", new EnumerationOptions
            {
                MatchCasing = MatchCasing.CaseInsensitive,
                RecurseSubdirectories = true
            });

            foreach (var f in files)
            {
                string jsRelativeFile = Path.GetFileName(f);
                string templateFilename = f.Substring(0, f.Length - 2) + "html";
                if (!File.Exists(templateFilename))
                    continue;

                Console.WriteLine($"Parsing: {f}");

                string js = File.ReadAllText(f);
                var matchDomIs = DomModuleIsPattern.Match(js);
                if (!matchDomIs.Success)
                {
                    WriteWarn($"\tNo DOM static is property found on class.");
                    continue;
                }

                var matchDomIsGroup = matchDomIs.Groups["is"];
                if (!matchDomIsGroup.Success)
                {
                    WriteWarn($"\tID string not found in {matchDomIs.Value}.");
                    continue;
                }


                string domModule = matchDomIsGroup.Value;
                Console.WriteLine($"\tDOM module: {domModule}");

                var htmlImports = new List<string>();
                string html = File.ReadAllText(templateFilename);
                foreach (Match htmlImport in HtmlImportPattern.Matches(html)) {
                    var href = htmlImport.Groups["href"];
                    if (href.Success)
                        htmlImports.Add(href.Value);
                }

                if (htmlImports.Count > 0)
                    Console.WriteLine($"\tHTML Imports: x{htmlImports.Count}");

                var scripts = new List<string>();
                int offset = 0;
                foreach (Match nestedScript in NestedScriptPattern.Matches(html)) {
                    var src = nestedScript.Groups["src"];
                    if (src.Success && 
                        !jsRelativeFile.Equals(src.Value, StringComparison.OrdinalIgnoreCase)) // Not the JS we found the HTML from
                        scripts.Add(src.Value);

                    string before = html.Substring(0, nestedScript.Index - offset);
                    string after = html.Substring(nestedScript.Index + nestedScript.Length - offset);
                    html = before + after;
                    offset += nestedScript.Length; // Strting now shorter by the removed length
                }

                if(scripts.Count > 0)
                    Console.WriteLine($"\tScripts: {string.Join(", ", scripts)}");

                string template = null;
                foreach (Match domTemplate in DomModuleTemplatePattern.Matches(html)) {
                    var id = domTemplate.Groups["id"];
                    if (!id.Success)
                        continue;

                    if (domModule != id.Value)
                        continue; // Not this module

                    var t = domTemplate.Groups["template"];
                    if (!t.Success)
                        break; // We know the ID matches, but template not found

                    template = t.Value;
                }

                if (string.IsNullOrEmpty(template)) {
                    WriteWarn($"\tNo <template> with id matching {domModule} found in HTML.");
                    continue;
                }


                WriteSuccess($"\tTemplate: {template.Length}");
            }

            Console.ReadLine();
        }
    }
}
