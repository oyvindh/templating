﻿using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Utils;

namespace dotnet_new3
{
    public static class TemplateCreator
    {
        public static async Task<int> Instantiate(CommandLineApplication app, string templateName, CommandOption name, CommandOption dir, CommandOption help, CommandOption alias, IReadOnlyDictionary<string, string> inputParameters, bool quiet, bool skipUpdateCheck)
        {
            if(string.IsNullOrWhiteSpace(templateName) && help.HasValue())
            {
                app.ShowHelp();
                return 0;
            }

            ITemplateInfo tmpltInfo;

            using (Timing.Over("Get single"))
            {
                if (!Microsoft.TemplateEngine.Edge.Template.TemplateCreator.TryGetTemplate(templateName, out tmpltInfo))
                {
                    return -1;
                }
            }

            ITemplate tmplt = SettingsLoader.LoadTemplate(tmpltInfo);

            if (!skipUpdateCheck)
            {
                if (!quiet)
                {
                    Reporter.Output.WriteLine("Checking for updates...");
                }

                //TODO: Implement check for updates over mount points
                //bool updatesReady;

                //if (tmplt.Source.ParentSource != null)
                //{
                //    updatesReady = await tmplt.Source.Source.CheckForUpdatesAsync(tmplt.Source.ParentSource, tmplt.Source.Location);
                //}
                //else
                //{
                //    updatesReady = await tmplt.Source.Source.CheckForUpdatesAsync(tmplt.Source.Location);
                //}

                //if (updatesReady)
                //{
                //    Console.WriteLine("Updates for this template are available. Install them now? [Y]");
                //    string answer = Console.ReadLine();

                //    if (string.IsNullOrEmpty(answer) || answer.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase))
                //    {
                //        string packageId = tmplt.Source.ParentSource != null
                //            ? tmplt.Source.Source.GetInstallPackageId(tmplt.Source.ParentSource, tmplt.Source.Location)
                //            : tmplt.Source.Source.GetInstallPackageId(tmplt.Source.Location);

                //        Command.CreateDotNet("new3", new[] { "-u", packageId, "--quiet" }).ForwardStdOut().ForwardStdErr().Execute();
                //        Command.CreateDotNet("new3", new[] { "-i", packageId, "--quiet" }).ForwardStdOut().ForwardStdErr().Execute();

                //        Program.Broker.ComponentRegistry.ForceReinitialize();

                //        if (!TryGetTemplate(templateName, source, quiet, out tmplt))
                //        {
                //            return -1;
                //        }

                //        generator = tmplt.Generator;
                //    }
                //}
            }

            string realName = name.Value() ?? tmplt.DefaultName ?? new DirectoryInfo(Directory.GetCurrentDirectory()).Name;
            string currentDir = Directory.GetCurrentDirectory();
            bool missingProps = false;

            if (dir.HasValue())
            {
                Directory.SetCurrentDirectory(Directory.CreateDirectory(realName).FullName);
            }

            IParameterSet templateParams = tmplt.Generator.GetParametersForTemplate(tmplt);

            // Setup the default parameter values provided by the template.
            foreach (ITemplateParameter param in templateParams.ParameterDefinitions)
            {
                if (param.IsName)
                {
                    templateParams.ResolvedValues[param] = realName;
                }
                else if (param.Priority != TemplateParameterPriority.Required && param.DefaultValue != null)
                {
                    templateParams.ResolvedValues[param] = param.DefaultValue;
                }
            }

            if (alias.HasValue())
            {
                //TODO: Add parameters to aliases (from _parameters_ collection)
                AliasRegistry.SetTemplateAlias(alias.Value(), tmplt);
                Reporter.Output.WriteLine("Alias created.");
                return 0;
            }

            // Override the template defaults with user input parameter values.
            foreach (KeyValuePair<string, string> inputParam in inputParameters)
            {
                ITemplateParameter paramFromTemplate;
                if (templateParams.TryGetParameterDefinition(inputParam.Key, out paramFromTemplate))
                {
                    // The user provided params included the name of a bool flag without a value.
                    // We assume that means the value "true" is desired for the bool.
                    // This must happen here, as opposed to GlobalRunSpec.ProduceUserVariablesCollection()
                    if (inputParam.Value == null)
                    {
                        if (paramFromTemplate.DataType == "bool")
                        {
                            templateParams.ResolvedValues[paramFromTemplate] = "true";
                        }
                        else
                        {
                            throw new TemplateParamException(inputParam.Key, null, paramFromTemplate.DataType);
                        }
                    }
                    else
                    {
                        templateParams.ResolvedValues[paramFromTemplate] = inputParam.Value;
                    }
                }
            }

            foreach (ITemplateParameter parameter in templateParams.ParameterDefinitions)
            {
                if (!help.HasValue() && parameter.Priority == TemplateParameterPriority.Required && !templateParams.ResolvedValues.ContainsKey(parameter))
                {
                    Reporter.Error.WriteLine($"Missing required parameter {parameter.Name}".Bold().Red());
                    missingProps = true;
                }
            }

            if (help.HasValue() || missingProps)
            {
                string val;
                if (tmplt.TryGetProperty("Description", out val))
                {
                    Reporter.Output.WriteLine($"{val}");
                }

                if (tmplt.TryGetProperty("Author", out val))
                {
                    Reporter.Output.WriteLine($"Author: {val}");
                }

                if (tmplt.TryGetProperty("DiskPath", out val))
                {
                    Reporter.Output.WriteLine($"Disk Path: {val}");
                }

                Reporter.Output.WriteLine("Parameters:");
                foreach (ITemplateParameter parameter in tmplt.Generator.GetParametersForTemplate(tmplt).ParameterDefinitions.OrderBy(x => x.Priority).ThenBy(x => x.Name))
                {
                    Reporter.Output.WriteLine(
                        $@"    {parameter.Name} ({parameter.Priority})
        Type: {parameter.Type}");

                    if (!string.IsNullOrEmpty(parameter.Documentation))
                    {
                        Reporter.Output.WriteLine($"        Documentation: {parameter.Documentation}");
                    }

                    if (!string.IsNullOrEmpty(parameter.DefaultValue))
                    {
                        Reporter.Output.WriteLine($"        Default: {parameter.DefaultValue}");
                    }
                }

                return missingProps ? -1 : 0;
            }

            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                //TODO: Pass an implementation of ITemplateEngineHost
                await tmplt.Generator.Create(null, tmplt, templateParams);
                sw.Stop();

                if (!quiet)
                {
                    Reporter.Output.WriteLine($"Content generated in {sw.Elapsed.TotalMilliseconds} ms");
                }
            }
            finally
            {
                Directory.SetCurrentDirectory(currentDir);
            }
            return 0;
        }
    }
}
