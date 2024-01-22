using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Evolits.CommonCodegen
{
    [Generator]
    public class DatabaseInterfaceSourceGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new MainSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var receiver = (MainSyntaxReceiver)context.SyntaxReceiver;
            foreach (KeyValuePair<ClassDeclarationSyntax, string> interfaceClassNamespacePair in receiver.InterfaceClassNamespacePairs)
            {
                //Find entity classes that belong to the iterated interface.
                List<ClassDeclarationSyntax> entityClasses = new List<ClassDeclarationSyntax>();
                foreach (KeyValuePair<ClassDeclarationSyntax, string> entityClassInterfaceClassNamePair in receiver.EntityClassInterfaceClassNamePairs)
                {
                    if (entityClassInterfaceClassNamePair.Value !=
                        interfaceClassNamespacePair.Key.Identifier.ToString()) continue;
                    entityClasses.Add(entityClassInterfaceClassNamePair.Key);
                }
            
                //Generated partial class.
                var typeName = interfaceClassNamespacePair.Key.Identifier.ToString();
                const string newLine = "\n";
                string source;
                source = "using Microsoft.EntityFrameworkCore;" + newLine;
                source += newLine;
                source += "namespace " + interfaceClassNamespacePair.Value + ";" + newLine;
                source += newLine;
                source += "public partial class " + typeName + newLine;
                source += "{" + newLine;
                foreach (ClassDeclarationSyntax entity in entityClasses)
                {
                    source += "\t" + "public DbSet<" + entity.Identifier + "> " + entity.Identifier + "Set" + " { get; set; }" + newLine;
                }
                source += "}" + newLine;
                context.AddSource($"{typeName}.g.cs", source);
            }
        }
    }

    public class MainSyntaxReceiver : ISyntaxReceiver
    {
        public readonly Dictionary<ClassDeclarationSyntax, string> EntityClassInterfaceClassNamePairs = new Dictionary<ClassDeclarationSyntax, string>();

        public readonly Dictionary<ClassDeclarationSyntax, string> InterfaceClassNamespacePairs = new Dictionary<ClassDeclarationSyntax, string>();
    
        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            CheckForAttribute(syntaxNode);
            CheckForInterfaceClass(syntaxNode);
        }

        private void CheckForAttribute(SyntaxNode syntaxNode)
        {
            //Get database entity attributed classes.
            if (!(syntaxNode is ClassDeclarationSyntax classSyntax)) return;
            SyntaxNode attributeList = classSyntax.ChildNodes().FirstOrDefault(node => node is AttributeListSyntax);
            if (attributeList == null) return;
            SyntaxNode attribute = attributeList.ChildNodes().FirstOrDefault(node => node is AttributeSyntax);
            if (attribute == null) return;
            if (((AttributeSyntax)attribute).Name.ToString() != "DatabaseEntity") return;
        
            //Get their interface class names.
            const string startPhrase = "typeof(";
            const string endPhrase = ")";
            var argumentList = ((AttributeSyntax)attribute).ArgumentList?.ToString();
            if (argumentList == null) return;
            if (!argumentList.Contains(startPhrase)) return;
            int startIndex = argumentList.IndexOf(startPhrase, StringComparison.Ordinal) + startPhrase.Length;
            int endIndex = argumentList.LastIndexOf(endPhrase, StringComparison.Ordinal);
            string interfaceClassName = argumentList.Substring(startIndex, endIndex - startIndex - 1);

            EntityClassInterfaceClassNamePairs.Add(classSyntax, interfaceClassName);
        }

        private void CheckForInterfaceClass(SyntaxNode syntaxNode)
        {
            if (!(syntaxNode is ClassDeclarationSyntax classSyntax)) return;
            SyntaxNode baseList = classSyntax.ChildNodes().FirstOrDefault(node => node is BaseListSyntax);
            if (baseList == null) return;
            if (!baseList.ChildNodes().Any(node =>
                    node is SimpleBaseTypeSyntax baseType && baseType.Type.ToString() == "DatabaseInterface")) return;
            if (classSyntax.Parent is FileScopedNamespaceDeclarationSyntax namespaceSyntax)
            {
                InterfaceClassNamespacePairs.Add(classSyntax, namespaceSyntax.Name.ToString());
            }
            else
            {
                InterfaceClassNamespacePairs.Add(classSyntax, null);
            }
        }
    }
}