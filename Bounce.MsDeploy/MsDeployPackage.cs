﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using Bounce.Config;
using Bounce.Framework;
using FS;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;

namespace Bounce.MsDeploy
{
    public class MsDeployPackage
    {
        private readonly ILog _log;
        private readonly IShell _shell;
        private readonly TemplateConfigurer _config;
        private readonly IOutput _output;
        private bool _verbose;

        public MsDeployPackage(ILog log, IShell shell, TemplateConfigurer config, IOutput output)
        {
            _log = log;
            _shell = shell;
            _config = config;
            _output = output;
        }

        public void Deploy(string package, string webProject, Dictionary<string, object> environment, string username = "", string password = "", bool verbose = false)
        {
            _verbose = verbose;

            var archive = ConfiguredMsDeployArchiveDirectory(package, webProject, environment);
            var servers = Servers(environment);

            foreach (var server in servers)
            {
                var site = environment["site"];
                _log.Info("deploying to server: {0}, site: {1}", server, site);

                var args = new ExecArgsBuilder();
                args.Add("-source:archiveDir='{0}'", archive);
                args.Add(
                    "-dest:auto,computerName='http://{0}/MSDeployAgentService',includeAcls='False',username='{1}',password='{2}',authtype=ntlm",
                    server, username, password);
                args.Add("-verb:sync");
                args.Add("-disableLink:AppPoolExtension");
                args.Add("-disableLink:ContentExtension");
                args.Add("-disableLink:CertificateExtension");
                args.Add(@"-setParam:""IIS Web Application Name""=""{0}""", site);
                _shell.Exec(@"msdeploy", args.Args);
            }
        }

        private string ConfiguredMsDeployArchiveDirectory(string zipPackage, string webProject, Dictionary<string, object> environment)
        {
            var archiveDir = CreateTemporaryDirectory();
            ExtractZipFile(zipPackage, archiveDir);

            var configFile = ConfigFileIn(archiveDir);
            var templateFile = Path.Combine(webProject, "web.template.config");
            _config.ConfigureFile(templateFile, environment, configFile);

            RemoveWebConfigParameters(archiveDir);

            return archiveDir;
        }

        private void RemoveWebConfigParameters(string archiveDir)
        {
            var parametersFile = Path.Combine(archiveDir, "parameters.xml");
            var parameters = XDocument.Load(parametersFile);
            var xmlParameterEntries = ((IEnumerable)parameters.Root.XPathEvaluate(@"//parameter[parameterEntry/@kind=""XmlFile""]")).Cast<XElement>();

            foreach (var xmlParameterEntry in xmlParameterEntries)
            {
                _log.Info("removing msdeploy package parameter: " + xmlParameterEntry.Attribute("name").Value);
                xmlParameterEntry.Remove();
            }

            parameters.Save(parametersFile);
        }

        private string CreateTemporaryDirectory()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }
        
        private void ExtractZipFile(string zipPackage, string archive)
        {
            var events = new FastZipEvents();

            if (_verbose)
            {
                events.ProcessFile = ProcessFile;
            }

            new FastZip(events).ExtractZip(zipPackage, archive, null);
        }

        private void ProcessFile(object sender, ScanEventArgs e)
        {
            _output.Output("Processing file: " + e.Name);
        }

        private static string ConfigFileIn(string archive)
        {
            return new FileSystem().Find(archive).First(IsWebConfig);
        }

        private static bool IsWebConfig(string path)
        {
            return Path.GetFileName(path).Equals("Web.config", StringComparison.InvariantCultureIgnoreCase);
        }

        private static IEnumerable<string> Servers(Dictionary<string, object> environmentVariables)
        {
            var environmentVariable = environmentVariables.ContainsKey("servers")
                                          ? environmentVariables["servers"]
                                          : environmentVariables["server"];
            return environmentVariable.ToString().Split(',').Select(s => s.Trim());
        }
    }
}
