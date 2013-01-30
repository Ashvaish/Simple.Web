﻿namespace Simple.Web.Razor
{
    using System;
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Web.Razor;

    using Microsoft.CSharp;

    using Simple.Web.Razor.Engine;

    internal class RazorTypeBuilder
    {
        internal const string TempAssemblyPrefix = "SimpleView_";

        private static readonly IDictionary<String, String> CompilerProperties =
            new Dictionary<String, String> { { "CompilerVersion", "v4.0" } };

        private static readonly string[] ExcludedReferencesOnMono =
            new[] { "System", "System.Core", "Microsoft.CSharp", "mscorlib" };

        private static readonly Func<Assembly, bool> IsValidReference = an =>
                ((Type.GetType("Mono.Runtime") == null) || !ExcludedReferencesOnMono.Any(an.Location.Contains));

        public Type CreateType(TextReader reader)
        {
            return CreateTypeImpl(reader);
        }

        private static Type CreateTypeImpl(TextReader reader)
        {
            var assemblyName = Path.Combine(Path.GetTempPath(), string.Format("{0}{1}.dll", RazorTypeBuilder.TempAssemblyPrefix, Guid.NewGuid().ToString("N")));

            var compilerParameters = CreateCompilerParameters(ref reader, assemblyName);
            var engine = CreateRazorTemplateEngine();
            var razorResult = engine.GenerateCode(reader);
            var compilerResults = CompileView(razorResult, compilerParameters);
            var assembly = compilerResults.CompiledAssembly;

            return assembly.GetExportedTypes().FirstOrDefault();
        }

        private static RazorTemplateEngine CreateRazorTemplateEngine()
        {
            var language = new CSharpRazorCodeLanguage();
            var host = new SimpleRazorEngineHost(language);
            var engine = new RazorTemplateEngine(host);

            return engine;
        }

        private static CompilerParameters CreateCompilerParameters(ref TextReader reader, string outputAssemblyName)
        {
            var compilerParameters =
                new CompilerParameters()
                {
                    GenerateExecutable = false,
                    GenerateInMemory = true,
                    TreatWarningsAsErrors = false,
                    OutputAssembly = outputAssemblyName
                };

            var declarationAssemblies = FindDeclarationAssemblies(ref reader);

            compilerParameters.ReferencedAssemblies.AddRange(
                TypeResolver.DefaultAssemblies
                .Union(declarationAssemblies)
                .Where(an => IsValidReference(an))
                .Select(an => an.Location).ToArray());

            return compilerParameters;
        }

        private static CompilerResults CompileView(GeneratorResults razorResult, CompilerParameters compilerParameters)
        {
            var codeProvider = new CSharpCodeProvider(CompilerProperties);
            var result = codeProvider.CompileAssemblyFromDom(compilerParameters, razorResult.GeneratedCode);

            if (result.Errors != null && result.Errors.HasErrors)
            {
                throw new RazorCompilerException(result.Errors.OfType<CompilerError>().Where(x => !x.IsWarning));
            }

            var assembly = Assembly.LoadFrom(compilerParameters.OutputAssembly);

            if (assembly == null)
            {
                throw new RazorCompilerException("Unable to load template assembly.");
            }

            if (assembly.GetType(SimpleRazorConfiguration.Namespace + "." + SimpleRazorConfiguration.ClassName) == null)
            {
                throw new RazorCompilerException("Unable to load template assembly.");
            }

            return result;
        }

        private static IEnumerable<Assembly> FindDeclarationAssemblies(ref TextReader reader)
        {
            Type model;
            Type handler;

            CreateDeclarationTypes(ref reader, out handler, out model);

            return GetNormalizedAssemblies(
                new Type[] { model, handler }
                    .Concat(model != null && model.IsGenericType ? model.GetGenericArguments() : new Type[0]).ToArray())
                    .GroupBy(an => an.Location)
                    .Select(an => an.First())
                    .ToArray();
        }

        private static IEnumerable<Assembly> GetNormalizedAssemblies(params Type[] types)
        {
            return from type in types where type != null select type.Assembly;
        }

        private static void CreateDeclarationTypes(ref TextReader reader, out Type handlerType, out Type modelType)
        {
            modelType = TypeHelper.ExtractType(ref reader, "@model");
            handlerType = TypeHelper.ExtractType(ref reader, "@handler");
        }
    }

    internal static class TypeHelper
    {
        private static readonly TypeResolver TypeResolver = new TypeResolver();

        internal static Type ExtractType(ref TextReader reader, string directive)
        {
            Type type = null;

            using (var writer = new StringWriter())
            {
                while (reader.Peek() > -1)
                {
                    var line = reader.ReadLine();
                    if (type == null && line != null && line.Trim().StartsWith(directive))
                    {
                        type = FindTypeFromRazorLine(line, directive);
                    }

                    writer.WriteLine(line);
                }


                using (Interlocked.CompareExchange(ref reader, new StringReader(writer.ToString()), reader))
                {
                }
            }

            return type;
        }

        private static Type FindTypeFromRazorLine(string line, string directive)
        {
            string typeName = line.Replace(directive, string.Empty).Trim();
            return TypeHelper.TypeResolver.FindType(typeName);
        }
    }
}
