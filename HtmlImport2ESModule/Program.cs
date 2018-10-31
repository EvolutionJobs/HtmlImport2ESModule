using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

namespace HtmlImport2ESModule
{
    class Program
    {
        /// <summary>Look for: &lt;link rel="import" href="target"&gt;</summary>
        static readonly Regex HtmlImportPattern = new Regex(
            @"<link.*\s+rel=(""import""|import)\s+href=""(?<href>[^>]+)""\s*\/?>",
            RegexOptions.IgnoreCase | RegexOptions.ECMAScript | RegexOptions.Multiline);

        /// <summary>Look for: static get is() { return 'target'; } </summary>
        static readonly Regex DomModuleIsPattern = new Regex(
            @"static\s+get\s+is\s*\(\s*\)\s*\{\s*return\s*['|""](?<is>\w+(?>\-\w+)+)['|""]\s*\;*\s*}",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        /// <summary>Look for: &lt;script src="target"&gt;&lt;/script&gt;</summary>
        static readonly Regex NestedScriptPattern = new Regex(
            @"\<script\s+.*src=""(?<src>[\w\-\.\d]+)"".*>\s*<\/script>",
            RegexOptions.IgnoreCase | RegexOptions.ECMAScript);

        /// <summary>Look for Polymer 2 &lt;dom-module&gt;&lt;template&gt; contents.</summary>
        static readonly Regex DomModuleTemplatePattern = new Regex(
            @"\<dom\-module\s+(?>strip-whitespace\s+)?id=\""(?<id>[\w\-]+)\""(?>\s+strip-whitespace)?\s*\>\s*<template(?>\s+strip-whitespace)?>\s*(?<template>.*)\s*<\/template>\s*<\/dom-module>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        /// <summary>Look for: //@ts-check directive.</summary>
        static readonly Regex TSCheckPattern = new Regex(
            @"^\s*\/\/\s*@ts\-check\s*$",
            RegexOptions.ECMAScript | RegexOptions.Multiline);

        /// <summary>Look for: &lt;style include="target"&gt;target&lt;/style&gt;</summary>
        static readonly Regex StylePattern = new Regex(
            @"<style\s*(?>include=""(?<include>[\w \-]*)"")?\s*>\s*(?<css>.*)\s*<\/style>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        static void WriteColour(string message, ConsoleColor background, ConsoleColor foreground)
        {
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

            string library = "lib";
            if (args?.Length > 1)
                library = args[1];

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
                foreach (Match htmlImport in HtmlImportPattern.Matches(html))
                {
                    var href = htmlImport.Groups["href"];
                    if (href.Success)
                        htmlImports.Add(href.Value);
                }

                if (htmlImports.Count > 0)
                    Console.WriteLine($"\tHTML Imports: x{htmlImports.Count}");

                var scripts = new List<string>();
                int offset = 0;
                foreach (Match nestedScript in NestedScriptPattern.Matches(html))
                {
                    var src = nestedScript.Groups["src"];
                    if (src.Success &&
                        !jsRelativeFile.Equals(src.Value, StringComparison.OrdinalIgnoreCase)) // Not the JS we found the HTML from
                        scripts.Add(src.Value);

                    string before = html.Substring(0, nestedScript.Index - offset);
                    string after = html.Substring(nestedScript.Index + nestedScript.Length - offset);
                    html = before + after;
                    offset += nestedScript.Length; // Strting now shorter by the removed length
                }

                if (scripts.Count > 0)
                    Console.WriteLine($"\tScripts: {string.Join(", ", scripts)}");

                string template = null;
                foreach (Match domTemplate in DomModuleTemplatePattern.Matches(html))
                {
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

                if (string.IsNullOrEmpty(template))
                {
                    WriteWarn($"\tNo <template> with id matching {domModule} found in HTML.");
                    continue;
                }

                // Get the relative location of the library to the HTML imports, and figure out the path to the new ES Modules from that
                string relativeLibDir = $"../../{library}/"; // best guess
                if (htmlImports.Count > 0)
                {
                    string libInPath = $"/{library}/";
                    foreach (var i in htmlImports)
                    {
                        var idx = i.IndexOf(libInPath);
                        if (idx < 0)
                            continue;

                        relativeLibDir = i.Substring(0, idx + libInPath.Length);
                        break;
                    }
                }

                // Find <style includes="old-styles">...
                // turn into const styles = `{oldStyles}...
                string styles = "";
                string[] includeStyles = new string[0];
                var styleMatch = StylePattern.Match(template);
                if (styleMatch.Success) {
                    var includeMatch = styleMatch.Groups["include"];
                    if (includeMatch.Success) {
                        includeStyles = includeMatch.Value.Split(' ');
                        styles += string.Join("", includeStyles.Select(i => "${" + DashToCamelCase(i) + "}")) + "\r\n";
                    }

                    var cssMatch = styleMatch.Groups["css"];
                    if (cssMatch.Success)
                        styles += $@"<style>
    {cssMatch.Value}
</style>";

                    template = 
                        template.Substring(0, styleMatch.Index) + 
                        template.Substring(styleMatch.Index + styleMatch.Length);
                }

                // Get string before and after where we'll insert the template
                string beforeJS = js.Substring(0, matchDomIs.Index + matchDomIs.Length);
                string afterJS = js.Substring(matchDomIs.Index + matchDomIs.Length);

                // In the before remove and //@ts-check, as we'll add at start
                beforeJS = TSCheckPattern.Replace(beforeJS, "");

                // Replace the classname.
                beforeJS = beforeJS.Replace("Polymer.Element", "PolymerElement");

                // Put it all together - TS, Polymer 3 element, repurposed HTML imports, existing scripts, and add the template after the 
                string jsCombined = $@"// @ts-check
import {{ PolymerElement, html }} from '{relativeLibDir}@polymer/polymer/polymer-element.js';
{string.Join('\n', htmlImports.Where(NotDeprecated).Select(i => HtmlResourceToESModule(i, includeStyles)))}
{string.Join('\n', scripts.Select(JSResourceToESModule))}

const styles = html`{styles}`;

{beforeJS}
    static get template() {{ return html`${{styles}}{template}`; }}
{afterJS}";
                // WriteColour(jsCombined, ConsoleColor.Gray, ConsoleColor.DarkGray);

                File.WriteAllText(f, jsCombined);
                File.Delete(templateFilename);
                WriteSuccess($"\tTemplate ({jsCombined.Length} chars) Moved from:\r\n\t\t{templateFilename} to\r\n\t\t{f}");
            }

            Console.ReadLine();
        }

        static string DashToCamelCase(string dash) =>
            new string(DashToCamelCaseImpl(dash).ToArray());

        static IEnumerable<char> DashToCamelCaseImpl(string dash)
        {
            bool afterDash = false;
            foreach (char c in dash)
            {
                if (c == '-')
                {
                    afterDash = true;
                    continue;
                }

                yield return afterDash ? char.ToUpper(c) : char.ToLower(c);
                afterDash = false;
            }
        }

        static bool NotDeprecated(string resource) {

            // Remove deprecated Polymer 2 controls
            if (resource.EndsWith("polymer.html") ||
                resource.EndsWith("polymer-element.html") ||
                resource.EndsWith("dom-repeat.html") ||
                resource.EndsWith("dom-if.html"))
                return false;

            return true;
        }

        static string HtmlResourceToESModule(string resource, string[] includeStyles)
        {
            string js = resource.Substring(0, resource.Length - 4) + "js";

            // If a style include the name as the default, as ES Module imported styles are explicit, not a side effect
            foreach (var styleImport in includeStyles)
                if (resource.EndsWith(styleImport + ".html"))
                    return $"import {DashToCamelCase(styleImport)} from '{ModulePrefix(js)}';";

            // Polymer 3 components are under @polymer
            js = js.Replace("/polymer/", "/@polymer/polymer/");
            js = js.Replace("/iron-", "/@polymer/iron-");
            js = js.Replace("/paper-", "/@polymer/paper-");

            return $"import '{ModulePrefix(js)}';";
        }

        static string JSResourceToESModule(string resource) =>
            $"import '{ModulePrefix(resource)}';";

        static string ModulePrefix(string resource) =>
            resource.StartsWith('.') ? resource : "./" + resource;
    }
}
