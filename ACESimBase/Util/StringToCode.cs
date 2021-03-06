﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using System.IO;
using Microsoft.CodeAnalysis.Emit;
using System.Reflection;
using Microsoft.CodeAnalysis;
using System.Linq;

namespace ACESimBase.Util
{
    public static class StringToCode
    {

        public static Type LoadCode(string codeString, string fullyQualifiedClassName, List<Type> extraTypesToReference = null)
        {
            int maxRetries = 10;
            int tryNum = 0;
            retry:
            try
            {
                return LoadCodeHelper(codeString, fullyQualifiedClassName, extraTypesToReference);
            }
            catch (Exception ex)
            {
                if (++tryNum <= maxRetries)
                    goto retry;
                throw new Exception("LoadCode failed:" + ex.Message);
            }
        }

        public static Type LoadCodeHelper(string codeString, string fullyQualifiedClassName, List<Type> extraTypesToReference = null)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(codeString);
            string assemblyName = Path.GetRandomFileName();
            var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);
            List<MetadataReference> references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            };
            string trustedPlatformAssembliesString = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"));
            string[] trustedAssembliesPaths = trustedPlatformAssembliesString?.Split(Path.PathSeparator) ?? new string[0];
            var neededAssemblies = new[]
            {
                "System.Runtime",
                "mscorlib",
            };
            references.AddRange(trustedAssembliesPaths
                .Where(p => neededAssemblies.Contains(Path.GetFileNameWithoutExtension(p)))
                .Select(p => MetadataReference.CreateFromFile(p))
                .ToList());
            if (extraTypesToReference != null)
                foreach (Type t in extraTypesToReference)
                    references.Add(MetadataReference.CreateFromFile(t.Assembly.Location));

            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: new[] { tree },
                references: references.ToArray(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(false).WithOverflowChecks(false));
            using (var ms = new MemoryStream())
            {
                EmitResult result = compilation.Emit(ms);

                if (!result.Success)
                {
                    IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError ||
                        diagnostic.Severity == DiagnosticSeverity.Error);

                    foreach (Diagnostic diagnostic in failures)
                    {
                        Console.Error.WriteLine("{0}: {1}", diagnostic.Id, diagnostic.GetMessage());
                    }
                    throw new Exception("Failures occurred compiling code");
                }
                else
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    Assembly assembly = Assembly.Load(ms.ToArray());
                    return assembly.GetType(fullyQualifiedClassName);
                }
            }
        }
    }
}
