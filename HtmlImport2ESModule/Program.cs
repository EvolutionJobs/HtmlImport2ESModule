using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text;

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

        /// <summary>Look for: &lt;script&gt;target&lt;/script&gt;</summary>
        static readonly Regex ScriptPattern = new Regex(
            @"<script[\w\s\""\=]*>\s*(?<script>.*)\s*<\/script>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        #region console helpers

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

        #endregion

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

            int jsCount = 0, htmlCount = 0;
            foreach (var f in files)
            {
                jsCount++;
                string jsRelativeFile = Path.GetFileName(f);
                string templateFilename = f.Substring(0, f.Length - 2) + "html";
                if (!File.Exists(templateFilename))
                    continue; // No HTML template to import

                Console.WriteLine($"Parsing: {f}");

                string jsFile = File.ReadAllText(f);

                (string failReason, string js) = ParseHtmlImport(templateFilename, library, jsFile);
                if (!string.IsNullOrEmpty(failReason))
                {
                    WriteWarn("\t" + failReason);
                    continue;
                }
                

                //WriteColour(js, ConsoleColor.Gray, ConsoleColor.DarkGray);

                File.WriteAllText(f, js, Encoding.UTF8);
                File.Delete(templateFilename);
                WriteSuccess($"\tTemplate ({js.Length} chars) Moved from:\r\n\t\t{templateFilename} to\r\n\t\t{f}");
            }


            var htmlFiles = Directory.GetFileSystemEntries(target, "*.html", new EnumerationOptions
            {
                MatchCasing = MatchCasing.CaseInsensitive,
                RecurseSubdirectories = true
            });

            foreach (var f in htmlFiles)
            {
                htmlCount++;
                Console.WriteLine($"Parsing: {f}");
                (string failReason, string js) = ParseHtmlImport(f, library);
                if (!string.IsNullOrEmpty(failReason))
                {
                    WriteWarn("\t" + failReason);
                    continue;
                }

                //WriteColour(js, ConsoleColor.Gray, ConsoleColor.DarkGray);

                string jsOutputFilename = f.Substring(0, f.Length - 4) + "js";
                File.WriteAllText(jsOutputFilename, js, Encoding.UTF8);
                File.Delete(f);
                WriteSuccess($"\tTemplate ({js.Length} chars) Moved from:\r\n\t\t{f} to\r\n\t\t{jsOutputFilename}");
            }

            Console.WriteLine($"JS: {jsCount}, HTML: {htmlCount}");
            Console.ReadLine();
        }

        /// <summary>Parse JS for the DOM module property, and return content before and after the declaration.</summary>
        /// <param name="js">The JS to parse.</param>
        /// <returns>Tuple, holding fail reason (if any), module ID, before content and after content.</returns>
        static (string failReason, string domModule, string before, string after) ParseJSContent(string js) {
            var matchDomIs = DomModuleIsPattern.Match(js);
            if (!matchDomIs.Success)
                return ($"\tNo DOM static is property found on class.", null, null, null);

            var matchDomIsGroup = matchDomIs.Groups["is"];
            if (!matchDomIsGroup.Success)
                return ($"\tID string not found in {matchDomIs.Value}.", null, null, null);

            string domModule = matchDomIsGroup.Value;

            // Get string before and after where we'll insert the template
            string before = js.Substring(0, matchDomIs.Index + matchDomIs.Length);
            string after = js.Substring(matchDomIs.Index + matchDomIs.Length);

            // In the before remove and //@ts-check, as we'll add at start
            before = TSCheckPattern.Replace(before, "");

            // Replace the classname.
            before = before.Replace("Polymer.Element", "PolymerElement");

            return (null, domModule, before, after);
        }

        /// <summary>Parse a Polymer 2 HTML import file and output a Polymer 3</summary>
        /// <param name="templateFilename">The path to the HTML file to read.</param>
        /// <param name="library">Name of the folder to check for the library components.</param>
        /// <param name="js">Any JS read from the accompanying file.</param>
        /// <returns>The fail reason and the output JS with the embedded template.</returns>
        static (string failReason, string js) ParseHtmlImport(string templateFilename, string library, string js = "") {

            if (!File.Exists(templateFilename))
                return ("No DOM static is property found on class.", null);

            
            string html = File.ReadAllText(templateFilename);

            foreach (Match nestedScript in ScriptPattern.Matches(html))
            {
                var scriptMatch = nestedScript.Groups["script"];
                if (scriptMatch.Success)
                    js += "\r\n" + scriptMatch.Value;
            }

            if (string.IsNullOrEmpty(js))
                return ("No JS content found.", null);

            (string failReason, string domModule, string before, string after) = ParseJSContent(js);
            if(!string.IsNullOrEmpty(failReason))
                return (failReason, null);

            var htmlImports = new List<string>();
            foreach (Match htmlImport in HtmlImportPattern.Matches(html))
            {
                var href = htmlImport.Groups["href"];
                if (href.Success)
                    htmlImports.Add(href.Value);
            }

            var scripts = new List<string>();
            int offset = 0;
            foreach (Match nestedScript in NestedScriptPattern.Matches(html))
            {
                var src = nestedScript.Groups["src"];
                if (src.Success)
                    scripts.Add(src.Value);

                string beforeScr = html.Substring(0, nestedScript.Index - offset);
                string afterScr = html.Substring(nestedScript.Index + nestedScript.Length - offset);
                html = beforeScr + afterScr;
                offset += nestedScript.Length; // Strting now shorter by the removed length
            }

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
                return ($"No <template> with id matching {domModule} found in HTML.", null);

            // Find <style includes="old-styles">...
            // turn into const styles = `{oldStyles}...
            string styles = "";
            string[] includeStyles = new string[0];
            var styleMatch = StylePattern.Match(template);
            if (styleMatch.Success)
            {
                var includeMatch = styleMatch.Groups["include"];
                if (includeMatch.Success)
                {
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

            // Put it all together - TS, Polymer 3 element, repurposed HTML imports, existing scripts, and add the template after the 
            string result = $@"// @ts-check
import {{ PolymerElement, html }} from '{relativeLibDir}@polymer/polymer/polymer-element.js';
{string.Join('\n', htmlImports.Where(NotDeprecated).Select(i => HtmlResourceToESModule(i, includeStyles, library)))}
{string.Join('\n', scripts.Select(JSResourceToESModule))}

const styles = html`{styles}`;

{before}
    static get template() {{ return html`${{styles}}{template}`; }}
{after}";

            return (null, result);
        }

        /// <summary>Convert web component name covention to JS camelCase, so my-control becomes myControl.</summary>
        /// <param name="dash">The dash seperated web component name.</param>
        /// <returns>The camel case equivalent.</returns>
        static string DashToCamelCase(string dash) =>
            new string(DashToCamelCaseImpl(dash).ToArray());

        /// <summary>Loop through a string, making letters lowercase unless after a dash, in which case uppercase.
        /// Dashes are skipped.</summary>
        /// <param name="dash">The string to loop through.</param>
        /// <returns>Enumeration of chars.</returns>
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

        /// <summary>Return tru if this is one of the Polymer 2 iron- or paper- components we can still use in Polymer 3.</summary>
        /// <param name="resource">The resource to check.</param>
        /// <returns>True if still valid, false if deprecated.</returns>
        static bool NotDeprecated(string resource) {

            // Remove deprecated Polymer 2 controls
            if (resource.EndsWith("polymer.html") ||
                resource.EndsWith("polymer-element.html") ||
                resource.EndsWith("dom-repeat.html") ||
                resource.EndsWith("dom-if.html"))
                return false;

            return true;
        }

        /// <summary>Convert a referenced Polymer 2 HTML import to the equivalent Polymer 3 ES Module reference.</summary>
        /// <param name="resource">The resource to parse.</param>
        /// <param name="includeStyles">Styles ifrom include attributes in Polymer 2, which become imported parameters in Polymer 3.</param>
        /// <param name="library">The library dir we expect to find the components under.</param>
        /// <returns></returns>
        static string HtmlResourceToESModule(string resource, string[] includeStyles, string library)
        {
            // We have something.html, we want something.js
            string js = resource.Substring(0, resource.Length - 4) + "js";

            // If a style include the name as the default, as ES Module imported styles are explicit, not a side effect
            foreach (var styleImport in includeStyles)
                if (resource.EndsWith(styleImport + ".html"))
                    return $"import {DashToCamelCase(styleImport)} from '{ModulePrefix(js)}';";

            // Polymer 3 components are under @polymer, rather than directly in the dir.
            js = js.Replace($"/{library}/polymer/", $"/{library}/@polymer/polymer/");
            js = js.Replace($"/{library}/iron-", $"/{library}/@polymer/iron-");
            js = js.Replace($"/{library}/paper-", $"/{library}/@polymer/paper-");

            return $"import '{ModulePrefix(js)}';";
        }

        /// <summary>Convert a linked script src to an import statement with assumed side effects.
        /// The may break some JS resorces, as module imports are always strict.</summary>
        /// <param name="resource">The resource to reference.</param>
        /// <returns>The import statement.</returns>
        static string JSResourceToESModule(string resource) =>
            $"import '{ModulePrefix(resource)}';";

        /// <summary>Add a ./ prefix if needed.</summary>
        static string ModulePrefix(string resource) =>
            resource.StartsWith('.') ? resource : "./" + resource;
    }
}
