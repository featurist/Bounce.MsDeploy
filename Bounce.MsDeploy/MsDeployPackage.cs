using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using Bounce.Config;
using Bounce.Framework;
using FS;

namespace Bounce.MsDeploy
{
    public class MsDeployPackage
    {
        private readonly ILog _log;
        private readonly IShell _shell;
        private readonly TemplateConfigurer _config;

        public MsDeployPackage(ILog log, IShell shell, TemplateConfigurer config)
        {
            _log = log;
            _shell = shell;
            _config = config;
        }

        public void Deploy(string package, string webProject, Dictionary<string, object> environment, string username = "", string password = "")
        {
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

            var configFile = FindRootWebConfig(archiveDir);
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

        private static void ExtractZipFile(string zipPackage, string archive)
        {
            new ICSharpCode.SharpZipLib.Zip.FastZip().ExtractZip(zipPackage, archive, null);
        }

        private static IEnumerable<string> Servers(Dictionary<string, object> environmentVariables)
        {
            var environmentVariable = environmentVariables.ContainsKey("servers")
                                          ? environmentVariables["servers"]
                                          : environmentVariables["server"];
            return environmentVariable.ToString().Split(',').Select(s => s.Trim());
        }

        private string FindRootWebConfig(string archive)
        {
            return (
                    from directory in Directory.GetDirectories(archive) 
                    let webConfigFile = Directory.GetFiles(directory, "Web.config").SingleOrDefault() 
                    select webConfigFile ?? FindRootWebConfig(directory)
                ).FirstOrDefault();
        }
    }
}
