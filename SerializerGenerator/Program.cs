﻿using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.IO;
using Library;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using System.IO.Pipelines.Samples.Models;

namespace SerializerGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            var cu = GenerateSerializer(typeof(Person)).NormalizeWhitespace();
            //var cu = GenerateSerializer(typeof(Model)).NormalizeWhitespace();


            File.WriteAllText(@"..\Poc\generated.cs", cu.ToFullString());

            Console.WriteLine(cu.ToFullString());
        }


        public static CompilationUnitSyntax GenerateSerializer(Type t)
        {
            var cu = CompilationUnit();
            cu = cu.AddUsings(
            UsingDirective(IdentifierName("System")),
            UsingDirective(QualifiedName(QualifiedName(IdentifierName("System"), IdentifierName("IO")), IdentifierName("Pipelines"))),
            UsingDirective(QualifiedName(QualifiedName(IdentifierName("System"), IdentifierName("Text")), IdentifierName("Formatting"))),
            UsingDirective(QualifiedName(IdentifierName("System"), IdentifierName("Text"))));

            var c = ClassDeclaration("Serializer").WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.PartialKeyword)));

            var b = Block();
            b = b.AddStatements(EncodingLocal("Utf8"));
            b = WriteSerializer(t, b, IdentifierName("t"));
            b = MergeLiterals(b);
            var sc = new StringCollector();
            b = ConvertToSlices(b, sc);

            c = c.AddMembers(SpanField("span", sc.GetRawString()));

            foreach (var s in sc.strings)
            {
                var i = sc.GetOffset(s);
                c = c.AddMembers(SpanField("slice" + i.Item1, SpanSlice(i.Item1, i.Item2)));
            }

            c = c.AddMembers(SerializerMethod(b, t));
            cu = cu.AddMembers(c);

            return cu;
        }

        private static BlockSyntax ConvertToSlices(BlockSyntax b, StringCollector sc)
        {
            var cs = b.ChildNodes().ToList();
            var res = Block();

            foreach (var cc in cs)
            {
                if (cc is IfStatementSyntax f && f.Statement is BlockSyntax block)
                {
                    if (f.Else != null)
                        res = res.AddStatements(IfStatement(f.Condition, ConvertToSlices(block, sc), ElseClause(ConvertToSlices((BlockSyntax)f.Else.Statement, sc))));
                    else
                        res = res.AddStatements(IfStatement(f.Condition, ConvertToSlices(block, sc)));
                }
                else
                {
                    var s = GetLiteralString(cc);
                    if (s != null)
                    {
                        var slice = sc.GetOffset(s);
                        res = res.AddStatements(WriteSpanSlice(slice.Item1, slice.Item2));
                    }
                    else
                    {
                        res = res.AddStatements((StatementSyntax)cc);
                    }
                }
            }
            return res;
        }

        private static BlockSyntax MergeLiterals(BlockSyntax b)
        {
            var cs = b.ChildNodes().ToList();
            var res = Block();

            string laststr = "";

            foreach (var cc in cs)
            {
                if (cc is IfStatementSyntax f && f.Statement is BlockSyntax block)
                {
                    if (f.Else != null)
                        res = res.AddStatements(IfStatement(f.Condition, MergeLiterals(block), ElseClause(MergeLiterals((BlockSyntax)f.Else.Statement))));
                    else
                        res = res.AddStatements(IfStatement(f.Condition, MergeLiterals(block)));
                }
                else
                {
                    var s = GetLiteralString(cc);
                    if (s != null)
                        laststr += s;
                    else
                    {
                        if (laststr != "")
                            res = res.AddStatements(WriteLiteralString(laststr));
                        res = res.AddStatements((StatementSyntax)cc);
                        laststr = "";
                    }
                }
            }

            if (laststr != "")
                res = res.AddStatements(WriteLiteralString(laststr));

            return res;
        }

        private static string GetLiteralString(SyntaxNode syntaxNode)
        {
            var r = syntaxNode.ReplaceNodes(syntaxNode.DescendantNodes().OfType<LiteralExpressionSyntax>(), (o, n) => LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("")));
            if (r.IsEquivalentTo(WriteLiteralString("")))
                return syntaxNode.DescendantNodes().OfType<LiteralExpressionSyntax>().First().Token.ValueText;
            else
                return null;
        }

        private static BlockSyntax WriteSerializer(Type t, BlockSyntax b, ExpressionSyntax member)
        {
            b = b.AddStatements(WriteLiteralString("{"));

            var last = t.GetTypeInfo().GetProperties().Last();

            foreach (var prop in t.GetTypeInfo().GetProperties())
            {
                b = b.AddStatements(WriteLiteralString($"\"{prop.Name}\" : "));

                if (prop.PropertyType == typeof(string))
                {
                    var sb = Block();
                    sb = sb.AddStatements(WriteLiteralString("'"));
                    sb = sb.AddStatements(WriteAppendExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, member, IdentifierName(prop.Name))));
                    sb = sb.AddStatements(WriteLiteralString("'"));

                    b = b.AddStatements(IfStatement(BinaryExpression(
        SyntaxKind.NotEqualsExpression,
        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, member, IdentifierName(prop.Name)),
        LiteralExpression(SyntaxKind.NullLiteralExpression)),    sb));
                }
                else if (prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(Guid))
                {
                    b = b.AddStatements(WriteLiteralString("'"));
                    b = b.AddStatements(WriteToStringExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, member, IdentifierName(prop.Name))));
                    b = b.AddStatements(WriteLiteralString("'"));
                }
                else if (prop.PropertyType == typeof(bool))
                {
                    b = b.AddStatements(WriteBoolExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, member, IdentifierName(prop.Name))));
                }
                else if (prop.PropertyType == typeof(int) || prop.PropertyType == typeof(bool) || prop.PropertyType == typeof(float) || prop.PropertyType == typeof(double))
                    b = b.AddStatements(WriteAppendExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, member, IdentifierName(prop.Name))));
                else
                {
                    if (!prop.PropertyType.GetTypeInfo().IsValueType)
                    {
                        b = b.AddStatements(IfStatement(BinaryExpression(
        SyntaxKind.NotEqualsExpression,
        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, member, IdentifierName(prop.Name)),
        LiteralExpression(SyntaxKind.NullLiteralExpression)),
    WriteSerializer(prop.PropertyType, Block(), MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, member, IdentifierName(prop.Name)))));
                    }
                    else
                        b = WriteSerializer(prop.PropertyType, b, MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, member, IdentifierName(prop.Name)));
                }


                if (prop != last)
                    b = b.AddStatements(WriteLiteralString(","));
            }

            b = b.AddStatements(WriteLiteralString("}"));
            return b;
        }

        private static IfStatementSyntax WriteBoolExpression(ExpressionSyntax member)
        {
            return IfStatement(member, Block(WriteLiteralString("true"))).WithElse(ElseClause(Block(WriteLiteralString("false"))));
        }

        private static ExpressionStatementSyntax WriteToStringExpression(ExpressionSyntax member)
        {
            return WriteAppendExpression(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, member, IdentifierName("ToString"))));
        }

        private static MethodDeclarationSyntax SerializerMethod(BlockSyntax b, Type t)
        {
            return MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), "Serialize").WithParameterList(
                                    ParameterList(
                                        SeparatedList<ParameterSyntax>(
                                            new SyntaxNodeOrToken[]{
                                    Parameter(Identifier("wb")).WithType(IdentifierName("WritableBuffer")),
                                    Token(SyntaxKind.CommaToken),
                                    Parameter(Identifier("t")).WithType( ParseTypeName(t.FullName))})))
                                            .WithBody(b).WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))); ;
        }

        private static ExpressionStatementSyntax WriteLiteralString(string token)
        {
            return WriteAppendExpression(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(token)));
        }

        private static ExpressionStatementSyntax WriteAppendExpression(ExpressionSyntax expression)
        {
            return ExpressionStatement(
    InvocationExpression(
        MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            IdentifierName("wb"),
            IdentifierName("Append")))
    .WithArgumentList(
        ArgumentList(
            SeparatedList<ArgumentSyntax>(
                new SyntaxNodeOrToken[]{
                    Argument(expression),
                    Token(SyntaxKind.CommaToken),
                    Argument( IdentifierName("enc"))}))));
        }

        private static ExpressionStatementSyntax WriteSpanSlice(int start, int length)
        {
            return ExpressionStatement(
                InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("wb"),
                        IdentifierName("Write")))
                .WithArgumentList(
                    ArgumentList(
                        SingletonSeparatedList(
                            Argument(
                                IdentifierName("slice" + start))))));
        }

        private static InvocationExpressionSyntax SpanSlice(int start, int length)
        {
            return InvocationExpression(MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    IdentifierName("span"),
                                                    IdentifierName("Slice")))
                                            .WithArgumentList(
                                                ArgumentList(
                                                    SeparatedList<ArgumentSyntax>(
                                                        new SyntaxNodeOrToken[]{
                                    Argument(
                                        LiteralExpression(
                                            SyntaxKind.NumericLiteralExpression,
                                            Literal(start))),
                                    Token(SyntaxKind.CommaToken),
                                    Argument(
                                        LiteralExpression(
                                            SyntaxKind.NumericLiteralExpression,
                                            Literal(length)))})));
        }

        public static FieldDeclarationSyntax SpanField(string id, string text)
        {
            return SpanField(id, NewSpan(text));
        }

        public static FieldDeclarationSyntax SpanField(string id, ExpressionSyntax expression)
        {
            return FieldDeclaration(
                VariableDeclaration(
                    GenericName(Identifier("Span"))
                    .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(PredefinedType(Token(SyntaxKind.ByteKeyword))))))
                    .WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier(id)).WithInitializer(EqualsValueClause(expression)))))
                    .WithModifiers(TokenList(Token(SyntaxKind.StaticKeyword)));
            ;
        }

        public static LocalDeclarationStatementSyntax EncodingLocal(string enc)
        {
            return LocalDeclarationStatement(
    VariableDeclaration(
        IdentifierName("var"))
    .WithVariables(
        SingletonSeparatedList(
            VariableDeclarator(
                Identifier("enc"))
            .WithInitializer(
                EqualsValueClause(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("TextEncoder"),
                        IdentifierName(enc)))))));
        }

        private static ObjectCreationExpressionSyntax NewSpan(string text)
        {
            return NewSpan(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(text)));
        }

        private static ObjectCreationExpressionSyntax NewSpan(ExpressionSyntax expression)
        {
            return ObjectCreationExpression(GenericName(Identifier("Span"))
                                                 .WithTypeArgumentList(
                                                     TypeArgumentList(
                                                         SingletonSeparatedList<TypeSyntax>(
                                                             PredefinedType(
                                                                 Token(SyntaxKind.ByteKeyword))))))
                                             .WithArgumentList(
                                                 ArgumentList(
                                                     SingletonSeparatedList(
                                                         Argument(
                                                             InvocationExpression(
                                                                 MemberAccessExpression(
                                                                     SyntaxKind.SimpleMemberAccessExpression,
                                                                     MemberAccessExpression(
                                                                         SyntaxKind.SimpleMemberAccessExpression,
                                                                         IdentifierName("Encoding"),
                                                                         IdentifierName("UTF8")),
                                                                     IdentifierName("GetBytes")))
                                                             .WithArgumentList(
                                                                 ArgumentList(
                                                                     SingletonSeparatedList(
                                                                         Argument(
                                                                             expression))))))));
        }
    }

    public class StringCollector
    {
        Dictionary<string, int> lengths = new Dictionary<string, int>();
        public Dictionary<string, int> indecies = new Dictionary<string, int>();
        public List<string> strings = new List<string>();

        public Tuple<int, int> GetOffset(string value)
        {
            if (!lengths.ContainsKey(value))
            {
                strings.Add(value);
                var b = Encoding.UTF8.GetBytes(value);
                indecies[value] = lengths.Values.Sum();
                lengths[value] = b.Length;

            }

            return Tuple.Create(indecies[value], lengths[value]);
        }

        public string GetRawString()
        {
            StringBuilder sb = new StringBuilder();

            foreach (var str in strings)
                sb.Append(str);

            return sb.ToString();
        }

        public string GetBase64String()
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(GetRawString()));
        }
    }
}