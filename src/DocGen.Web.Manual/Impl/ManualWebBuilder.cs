﻿using System;
using System.IO;
using System.Threading.Tasks;
using DocGen.Core;
using DocGen.Core.Markdown;
using DocGen.Web.Manual.Internal;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;
using Statik.Files;
using Statik.Hosting;
using Statik.Mvc;
using Statik.Web;

namespace DocGen.Web.Manual.Impl
{
    public class ManualWebBuilder : IManualWebBuilder
    {
        readonly IServiceProvider _serviceProvider;
        readonly IYamlParser _yamlParser;

        public ManualWebBuilder(IServiceProvider serviceProvider,
            IYamlParser yamlParser)
        {
            _serviceProvider = serviceProvider;
            _yamlParser = yamlParser;
        }
        
        public async Task<IManualWeb> BuildManual(string contentDirectory)
        {
            var webBuilder = _serviceProvider.GetRequiredService<IWebBuilder>();
            
            if(!await Task.Run(() => Directory.Exists(contentDirectory)))
                throw new DocGenException($"Manual directory {contentDirectory} doesn't exist");

            var resourcesDirectory = Path.Combine(contentDirectory, "resources");
            if(await Task.Run(() => Directory.Exists(resourcesDirectory)))
                webBuilder.RegisterDirectory("/resources", resourcesDirectory);
         
            webBuilder.RegisterMvc("/", new
            {
                controller = "Manual",
                action = "Index"
            });
            webBuilder.RegisterMvc("/pdfreactor/output.pdf", new
            {
                controller = "PDFreactor",
                action = "Pdf"
            });
            webBuilder.RegisterMvc("/pdfreactor/output.zip", new
            {
                controller = "PDFreactor",
                action = "Archive"
            });
            webBuilder.RegisterMvc("/prince/output.pdf", new
            {
                controller = "Prince",
                action = "Pdf"
            });
            
            // Register our static files.
            webBuilder.RegisterDirectory("/Users/pknopf/git/docgen/src/DocGen.Web.Manual/Internal/Resources/wwwroot");

            CoversheetConfig coversheet = null;
            var sections = new ManualSectionStore();
            foreach (var markdownFile in await Task.Run(() => Directory.GetFiles(contentDirectory, "*.md")))
            {
                var content = await Task.Run(() => File.ReadAllText(markdownFile));
                var yaml = _yamlParser.ParseYaml(content);
                if (yaml.Yaml == null) yaml = new YamlParseResult(JsonConvert.DeserializeObject("{}"), yaml.Markdown);
                
                var type = "Content";
                if (!string.IsNullOrEmpty((string)yaml.Yaml.Type))
                {
                    type = yaml.Yaml.Type;
                }
                
                switch (type)
                {
                    case "Coversheet":
                        if(coversheet != null) throw new DocGenException("Multiple coversheets detected");
                        coversheet = new CoversheetConfig();
                        coversheet.ProductImage = yaml.Yaml.ProductImage;
                        coversheet.ProductLogo = yaml.Yaml.ProductLogo;
                        coversheet.Model = yaml.Yaml.Model;
                        coversheet.Text = yaml.Yaml.Text;
                        break;
                    case "Content":
                        var order = (int?)yaml.Yaml.Order;
                        sections.AddMarkdown(order ?? 0, yaml.Markdown, markdownFile);
                        break;
                    default:
                        throw new DocGenException("Unknown contente type");
                }
            }
            
            if(coversheet == null) throw new DocGenException("You must provide a coversheet");
            
            webBuilder.RegisterServices(services => {
                services.AddMvc();
                services.Configure<RazorViewEngineOptions>(options =>
                {
                    options.FileProviders.Add(new PhysicalFileProvider("/Users/pknopf/git/docgen/src/DocGen.Web.Manual/Internal/Resources"));
                });
                services.AddSingleton(sections);
                services.AddSingleton(coversheet);
                // These regitrations are so that our controllers can inject core DocGen services.
                DocGen.Core.Services.Register(services);
            });
            
            return new ManualWeb(webBuilder);
        }

        class ManualWeb : IManualWeb
        {
            readonly IWebBuilder _webBuilder;

            public ManualWeb(IWebBuilder webBuilder)
            {
                _webBuilder = webBuilder;
            }
            
            public IWebHost BuildWebHost(string appBase = null, int port = Statik.StatikDefaults.DefaultPort)
            {
                return _webBuilder.BuildWebHost(appBase, port);
            }

            public IVirtualHost BuildVirtualHost(string appBase = null)
            {
                return _webBuilder.BuildVirtualHost(appBase);
            }
        }
    }
}