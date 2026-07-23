using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Dev.Naamloos.Fennec.Sdk.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class MatrixListenerGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName =
        "Dev.Naamloos.Fennec.Sdk.Generation.GenerateMatrixListenerAttribute`1";

    private const string RunOnCapturedContextPropertyName =
        "RunOnCapturedContext";

    private static readonly SymbolDisplayFormat FullyQualifiedTypeFormat =
        new(
            globalNamespaceStyle:
                SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle:
                SymbolDisplayTypeQualificationStyle
                    .NameAndContainingTypesAndNamespaces,
            genericsOptions:
                SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions:
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
                | SymbolDisplayMiscellaneousOptions
                    .IncludeNullableReferenceTypeModifier);

    public void Initialize(
        IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(
            static postInitializationContext =>
            {
                postInitializationContext.AddSource(
                    "GenerateMatrixListenerAttribute.g.cs",
                    SourceText.From(
                        AttributeSource,
                        Encoding.UTF8));
            });

        var listenerDeclarations =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                AttributeMetadataName,
                static (node, _) =>
                    node is ClassDeclarationSyntax,
                static (attributeContext, cancellationToken) =>
                    CreateGenerationResult(
                        attributeContext,
                        cancellationToken));

        context.RegisterSourceOutput(
            listenerDeclarations,
            static (sourceContext, result) =>
            {
                if (result.Diagnostic is not null)
                {
                    sourceContext.ReportDiagnostic(
                        result.Diagnostic);

                    return;
                }

                if (result.Model is null)
                {
                    return;
                }

                EmitListener(
                    sourceContext,
                    result.Model);
            });
    }

    private static GenerationResult CreateGenerationResult(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (context.TargetSymbol is not
            INamedTypeSymbol listenerClass)
        {
            return GenerationResult.Empty;
        }

        var location =
            listenerClass.Locations.FirstOrDefault();

        if (listenerClass.TypeKind != TypeKind.Class)
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.TargetMustBeClass,
                    location,
                    listenerClass.Name));
        }

        if (!IsPartial(
                listenerClass,
                cancellationToken))
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.ClassMustBePartial,
                    location,
                    listenerClass.Name));
        }

        if (listenerClass.IsStatic)
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.ClassCannotBeStatic,
                    location,
                    listenerClass.Name));
        }

        if (listenerClass.IsGenericType)
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.GenericTargetNotSupported,
                    location,
                    listenerClass.Name));
        }

        if (listenerClass.ContainingType is not null)
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.NestedTargetNotSupported,
                    location,
                    listenerClass.Name));
        }

        if (listenerClass.IsAbstract)
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.AbstractTargetNotSupported,
                    location,
                    listenerClass.Name));
        }

        var attribute =
            context.Attributes.FirstOrDefault();

        if (attribute?.AttributeClass is not
            {
                TypeArguments.Length: 1
            } attributeClass)
        {
            return GenerationResult.Empty;
        }

        if (attributeClass.TypeArguments[0] is not
            INamedTypeSymbol listenerInterface)
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.ListenerMustBeInterface,
                    location,
                    attributeClass.TypeArguments[0]
                        .ToDisplayString()));
        }

        if (listenerInterface.TypeKind != TypeKind.Interface)
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.ListenerMustBeInterface,
                    location,
                    listenerInterface.ToDisplayString()));
        }

        var onUpdateMethods =
            GetOnUpdateMethods(listenerInterface);

        if (onUpdateMethods.Length == 0)
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.OnUpdateNotFound,
                    location,
                    listenerInterface.ToDisplayString()));
        }

        if (onUpdateMethods.Length > 1)
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.MultipleOnUpdateMethods,
                    location,
                    listenerInterface.ToDisplayString()));
        }

        var onUpdateMethod = onUpdateMethods[0];

        if (onUpdateMethod.IsStatic)
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.StaticOnUpdateNotSupported,
                    location,
                    listenerInterface.ToDisplayString()));
        }

        if (onUpdateMethod.IsGenericMethod)
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.GenericOnUpdateNotSupported,
                    location,
                    listenerInterface.ToDisplayString()));
        }

        if (HasUnsupportedInterfaceMembers(
                listenerInterface,
                onUpdateMethod))
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.AdditionalInterfaceMembers,
                    location,
                    listenerInterface.ToDisplayString()));
        }

        // MAUI-oriented default:
        // capture and use SynchronizationContext unless explicitly disabled.
        var runOnCapturedContext =
            GetRunOnCapturedContext(attribute);

        if (runOnCapturedContext &&
            !onUpdateMethod.ReturnsVoid)
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.ContextDispatchRequiresVoid,
                    location,
                    listenerInterface.ToDisplayString(),
                    onUpdateMethod.ReturnType.ToDisplayString()));
        }

        if (runOnCapturedContext &&
            onUpdateMethod.Parameters.Any(
                static parameter =>
                    parameter.RefKind is
                        RefKind.Ref or
                        RefKind.Out or
                        RefKind.In or
                        RefKind.RefReadOnlyParameter))
        {
            return GenerationResult.FromDiagnostic(
                Diagnostic.Create(
                    Diagnostics.ContextDispatchDoesNotSupportRefParameters,
                    location,
                    listenerInterface.ToDisplayString()));
        }

        var namespaceName =
            listenerClass.ContainingNamespace
                .IsGlobalNamespace
                ? null
                : listenerClass.ContainingNamespace
                    .ToDisplayString();

        var parameters =
            onUpdateMethod.Parameters
                .Select(CreateParameterModel)
                .ToImmutableArray();

        var model = new ListenerModel(
            namespaceName,
            GetAccessibility(listenerClass),
            EscapeIdentifier(listenerClass.Name),
            listenerInterface.ToDisplayString(
                FullyQualifiedTypeFormat),
            GetReturnTypeName(onUpdateMethod),
            onUpdateMethod.ReturnsVoid,
            parameters,
            runOnCapturedContext);

        return GenerationResult.FromModel(model);
    }

    private static string GetReturnTypeName(
    IMethodSymbol method)
    {
        if (method.ReturnsVoid ||
            method.ReturnType.SpecialType ==
            SpecialType.System_Void)
        {
            return "void";
        }

        return method.ReturnType.ToDisplayString(
            FullyQualifiedTypeFormat);
    }

    private static bool GetRunOnCapturedContext(
        AttributeData attribute)
    {
        foreach (var namedArgument in
                 attribute.NamedArguments)
        {
            if (namedArgument.Key !=
                RunOnCapturedContextPropertyName)
            {
                continue;
            }

            if (namedArgument.Value.Value is bool value)
            {
                return value;
            }
        }

        // Default to true for MAUI-focused listener usage.
        return true;
    }

    private static ImmutableArray<IMethodSymbol>
        GetOnUpdateMethods(
            INamedTypeSymbol listenerInterface)
    {
        var interfaces =
            listenerInterface.AllInterfaces
                .Concat(
                    new[] { listenerInterface });

        var methods =
            new List<IMethodSymbol>();

        foreach (var interfaceSymbol in interfaces)
        {
            foreach (var member in
                     interfaceSymbol.GetMembers("OnUpdate"))
            {
                if (member is not IMethodSymbol method)
                {
                    continue;
                }

                if (method.MethodKind !=
                    MethodKind.Ordinary)
                {
                    continue;
                }

                if (methods.Any(
                        existing =>
                            SymbolEqualityComparer.Default.Equals(
                                existing,
                                method)))
                {
                    continue;
                }

                methods.Add(method);
            }
        }

        return methods.ToImmutableArray();
    }

    private static bool HasUnsupportedInterfaceMembers(
        INamedTypeSymbol listenerInterface,
        IMethodSymbol selectedOnUpdateMethod)
    {
        var interfaces =
            listenerInterface.AllInterfaces
                .Concat(
                    new[] { listenerInterface });

        foreach (var interfaceSymbol in interfaces)
        {
            foreach (var member in
                     interfaceSymbol.GetMembers())
            {
                if (member.IsStatic)
                {
                    continue;
                }

                if (member is IMethodSymbol method)
                {
                    if (method.MethodKind !=
                        MethodKind.Ordinary)
                    {
                        continue;
                    }

                    if (SymbolEqualityComparer.Default.Equals(
                            method,
                            selectedOnUpdateMethod))
                    {
                        continue;
                    }

                    return true;
                }

                if (member is IPropertySymbol or
                    IEventSymbol)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ParameterModel CreateParameterModel(
        IParameterSymbol parameter)
    {
        return new ParameterModel(
            EscapeIdentifier(parameter.Name),
            parameter.Type.ToDisplayString(
                FullyQualifiedTypeFormat),
            parameter.RefKind,
            parameter.IsParams);
    }

    private static bool IsPartial(
        INamedTypeSymbol symbol,
        CancellationToken cancellationToken)
    {
        foreach (var syntaxReference in
                 symbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (syntaxReference.GetSyntax(cancellationToken)
                is not ClassDeclarationSyntax declaration)
            {
                continue;
            }

            if (declaration.Modifiers.Any(
                    SyntaxKind.PartialKeyword))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetAccessibility(
        INamedTypeSymbol symbol)
    {
        switch (symbol.DeclaredAccessibility)
        {
            case Accessibility.Public:
                return "public";

            case Accessibility.Internal:
                return "internal";

            case Accessibility.Private:
                return "private";

            case Accessibility.Protected:
                return "protected";

            case Accessibility.ProtectedAndInternal:
                return "private protected";

            case Accessibility.ProtectedOrInternal:
                return "protected internal";

            default:
                return "internal";
        }
    }

    private static string EscapeIdentifier(
        string identifier)
    {
        return SyntaxFacts.GetKeywordKind(identifier) !=
               SyntaxKind.None
            ? "@" + identifier
            : identifier;
    }

    private static void EmitListener(
        SourceProductionContext context,
        ListenerModel model)
    {
        var source =
            BuildSource(model);

        var hintName =
            BuildHintName(model);

        context.AddSource(
            hintName,
            SourceText.From(
                source,
                Encoding.UTF8));
    }

    private static string BuildHintName(
        ListenerModel model)
    {
        var qualifiedName =
            model.Namespace is null
                ? model.ClassName
                : model.Namespace + "." + model.ClassName;

        var sanitized =
            qualifiedName
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace(',', '_')
                .Replace(' ', '_')
                .Replace('@', '_');

        return sanitized +
               ".MatrixListener.g.cs";
    }

    private static string BuildSource(
        ListenerModel model)
    {
        var source =
            new StringBuilder();

        source.AppendLine(
            "// <auto-generated />");
        source.AppendLine(
            "#nullable enable");
        source.AppendLine();

        if (model.Namespace is not null)
        {
            source.Append("namespace ");
            source.Append(model.Namespace);
            source.AppendLine(";");
            source.AppendLine();
        }

        AppendClassDeclaration(
            source,
            model);

        source.AppendLine("{");

        AppendCallbackDelegate(
            source,
            model);

        source.AppendLine();

        source.AppendLine(
            "    private readonly Callback _callback;");

        if (model.RunOnCapturedContext)
        {
            source.AppendLine(
                "    private readonly global::System.Threading.SynchronizationContext? _synchronizationContext;");

            source.AppendLine(
                "    private int _disposed;");
        }

        source.AppendLine();

        AppendConstructor(
            source,
            model);

        source.AppendLine();

        AppendCreateFactory(
            source,
            model);

        if (model.RunOnCapturedContext)
        {
            source.AppendLine();

            AppendExplicitContextFactory(
                source,
                model);

            source.AppendLine();

            AppendDirectFactory(
                source,
                model);
        }

        source.AppendLine();

        AppendOnUpdate(
            source,
            model);

        if (model.RunOnCapturedContext)
        {
            source.AppendLine();

            AppendContextDispatch(
                source,
                model);

            source.AppendLine();

            AppendDispose(
                source);

            source.AppendLine();

            AppendDispatchState(
                source,
                model);
        }

        source.AppendLine("}");

        return source.ToString();
    }

    private static void AppendClassDeclaration(
        StringBuilder source,
        ListenerModel model)
    {
        source.Append(model.Accessibility);
        source.Append(" sealed partial class ");
        source.Append(model.ClassName);
        source.Append(" : ");
        source.Append(model.InterfaceName);

        if (model.RunOnCapturedContext)
        {
            source.Append(
                ", global::System.IDisposable");
        }

        source.AppendLine();
    }

    private static void AppendCallbackDelegate(
        StringBuilder source,
        ListenerModel model)
    {
        source.Append("    public delegate ");
        source.Append(model.ReturnType);
        source.Append(" Callback(");

        AppendParameterDeclarations(
            source,
            model.Parameters,
            includeParams: false);

        source.AppendLine(");");
    }

    private static void AppendConstructor(
        StringBuilder source,
        ListenerModel model)
    {
        source.Append("    private ");
        source.Append(model.ClassName);
        source.AppendLine("(");
        source.AppendLine(
            "        Callback callback" +
            (model.RunOnCapturedContext ? "," : ")"));

        if (model.RunOnCapturedContext)
        {
            source.AppendLine(
                "        global::System.Threading.SynchronizationContext? synchronizationContext)");
        }

        source.AppendLine("    {");
        source.AppendLine(
            "        _callback = callback ??");
        source.AppendLine(
            "            throw new global::System.ArgumentNullException(nameof(callback));");

        if (model.RunOnCapturedContext)
        {
            source.AppendLine();
            source.AppendLine(
                "        _synchronizationContext = synchronizationContext;");
        }

        source.AppendLine("    }");
    }

    private static void AppendCreateFactory(
        StringBuilder source,
        ListenerModel model)
    {
        source.Append("    public static ");
        source.Append(model.ClassName);
        source.AppendLine(" Create(Callback callback)");
        source.AppendLine("    {");

        source.Append("        return new ");
        source.Append(model.ClassName);
        source.AppendLine("(");
        source.AppendLine(
            "            callback" +
            (model.RunOnCapturedContext ? "," : ");"));

        if (model.RunOnCapturedContext)
        {
            source.AppendLine(
                "            global::System.Threading.SynchronizationContext.Current);");
        }

        source.AppendLine("    }");
    }

    private static void AppendExplicitContextFactory(
        StringBuilder source,
        ListenerModel model)
    {
        source.Append("    public static ");
        source.Append(model.ClassName);
        source.AppendLine(" Create(");
        source.AppendLine(
            "        Callback callback,");
        source.AppendLine(
            "        global::System.Threading.SynchronizationContext? synchronizationContext)");
        source.AppendLine("    {");

        source.Append("        return new ");
        source.Append(model.ClassName);
        source.AppendLine("(");
        source.AppendLine(
            "            callback,");
        source.AppendLine(
            "            synchronizationContext);");

        source.AppendLine("    }");
    }

    private static void AppendDirectFactory(
        StringBuilder source,
        ListenerModel model)
    {
        source.Append("    public static ");
        source.Append(model.ClassName);
        source.AppendLine(" CreateDirect(Callback callback)");
        source.AppendLine("    {");

        source.Append("        return new ");
        source.Append(model.ClassName);
        source.AppendLine("(");
        source.AppendLine(
            "            callback,");
        source.AppendLine(
            "            synchronizationContext: null);");

        source.AppendLine("    }");
    }

    private static void AppendOnUpdate(
        StringBuilder source,
        ListenerModel model)
    {
        source.Append("    public ");
        source.Append(model.ReturnType);
        source.Append(" OnUpdate(");

        AppendParameterDeclarations(
            source,
            model.Parameters,
            includeParams: true);

        source.AppendLine(")");
        source.AppendLine("    {");

        if (model.RunOnCapturedContext)
        {
            source.AppendLine(
                "        RunOnCapturedContext(");

            source.Append(
                "            () => _callback(");

            AppendArguments(
                source,
                model.Parameters);

            source.AppendLine("));");
        }
        else
        {
            source.Append("        ");

            if (!model.ReturnsVoid)
            {
                source.Append("return ");
            }

            source.Append("_callback(");

            AppendArguments(
                source,
                model.Parameters);

            source.AppendLine(");");
        }

        source.AppendLine("    }");
    }

    private static void AppendContextDispatch(
        StringBuilder source,
        ListenerModel model)
    {
        source.AppendLine(
            "    private void RunOnCapturedContext(");
        source.AppendLine(
            "        global::System.Action action)");
        source.AppendLine("    {");

        source.AppendLine(
            "        if (IsDisposed)");
        source.AppendLine("        {");
        source.AppendLine(
            "            return;");
        source.AppendLine("        }");
        source.AppendLine();

        source.AppendLine(
            "        if (_synchronizationContext is null ||");
        source.AppendLine(
            "            global::System.Object.ReferenceEquals(");
        source.AppendLine(
            "                global::System.Threading.SynchronizationContext.Current,");
        source.AppendLine(
            "                _synchronizationContext))");
        source.AppendLine("        {");

        // Recheck immediately before invoking.
        source.AppendLine(
            "            if (!IsDisposed)");
        source.AppendLine("            {");
        source.AppendLine(
            "                action();");
        source.AppendLine("            }");

        source.AppendLine();
        source.AppendLine(
            "            return;");
        source.AppendLine("        }");
        source.AppendLine();

        source.AppendLine(
            "        _synchronizationContext.Post(");
        source.AppendLine(
            "            static state =>");
        source.AppendLine(
            "            {");
        source.AppendLine(
            "                var dispatch =");
        source.AppendLine(
            "                    (DispatchState)state!;");
        source.AppendLine();

        source.AppendLine(
            "                if (dispatch.Listener.IsDisposed)");
        source.AppendLine(
            "                {");
        source.AppendLine(
            "                    return;");
        source.AppendLine(
            "                }");
        source.AppendLine();

        source.AppendLine(
            "                dispatch.Action();");
        source.AppendLine(
            "            },");
        source.AppendLine(
            "            new DispatchState(this, action));");

        source.AppendLine("    }");
        source.AppendLine();

        source.AppendLine(
            "    private bool IsDisposed =>");
        source.AppendLine(
            "        global::System.Threading.Volatile.Read(ref _disposed) != 0;");
    }

    private static void AppendDispose(
        StringBuilder source)
    {
        source.AppendLine(
            "    public void Dispose()");
        source.AppendLine("    {");
        source.AppendLine(
            "        global::System.Threading.Interlocked.Exchange(");
        source.AppendLine(
            "            ref _disposed,");
        source.AppendLine(
            "            1);");
        source.AppendLine("    }");
    }

    private static void AppendDispatchState(
        StringBuilder source,
        ListenerModel model)
    {
        source.AppendLine(
            "    private sealed class DispatchState");
        source.AppendLine("    {");

        source.AppendLine(
            "        public DispatchState(");

        source.Append("            ");
        source.Append(model.ClassName);
        source.AppendLine(" listener,");

        source.AppendLine(
            "            global::System.Action action)");
        source.AppendLine(
            "        {");
        source.AppendLine(
            "            Listener = listener;");
        source.AppendLine(
            "            Action = action;");
        source.AppendLine(
            "        }");
        source.AppendLine();

        source.Append("        public ");
        source.Append(model.ClassName);
        source.AppendLine(" Listener { get; }");

        source.AppendLine();

        source.AppendLine(
            "        public global::System.Action Action { get; }");

        source.AppendLine("    }");
    }

    private static void AppendParameterDeclarations(
        StringBuilder source,
        ImmutableArray<ParameterModel> parameters,
        bool includeParams)
    {
        for (var index = 0;
             index < parameters.Length;
             index++)
        {
            if (index > 0)
            {
                source.Append(", ");
            }

            var parameter =
                parameters[index];

            if (includeParams &&
                parameter.IsParams)
            {
                source.Append("params ");
            }

            source.Append(
                GetRefKindPrefix(
                    parameter.RefKind));

            source.Append(parameter.Type);
            source.Append(' ');
            source.Append(parameter.Name);
        }
    }

    private static void AppendArguments(
        StringBuilder source,
        ImmutableArray<ParameterModel> parameters)
    {
        for (var index = 0;
             index < parameters.Length;
             index++)
        {
            if (index > 0)
            {
                source.Append(", ");
            }

            var parameter =
                parameters[index];

            source.Append(
                GetRefKindPrefix(
                    parameter.RefKind));

            source.Append(parameter.Name);
        }
    }

    private static string GetRefKindPrefix(
        RefKind refKind)
    {
        switch (refKind)
        {
            case RefKind.Ref:
                return "ref ";

            case RefKind.Out:
                return "out ";

            case RefKind.In:
                return "in ";

            case RefKind.RefReadOnlyParameter:
                return "ref readonly ";

            default:
                return string.Empty;
        }
    }

    private const string AttributeSource =
        """
        // <auto-generated />
        #nullable enable

        namespace Dev.Naamloos.Fennec.Sdk.Generation;

        /// <summary>
        /// Generates a managed callback adapter for a Matrix UniFFI
        /// listener interface containing a single OnUpdate method.
        /// </summary>
        /// <typeparam name="TListener">
        /// The UniFFI listener interface to implement.
        /// </typeparam>
        [global::System.AttributeUsage(
            global::System.AttributeTargets.Class,
            AllowMultiple = false,
            Inherited = false)]
        internal sealed class GenerateMatrixListenerAttribute<TListener>
            : global::System.Attribute
            where TListener : class
        {
            /// <summary>
            /// Gets or sets whether callbacks should be dispatched to
            /// the SynchronizationContext captured by Create.
            ///
            /// The generator treats this as true when it is not
            /// explicitly specified.
            /// </summary>
            public bool RunOnCapturedContext { get; init; } = true;
        }
        """;

    private sealed class ListenerModel
    {
        public ListenerModel(
            string? @namespace,
            string accessibility,
            string className,
            string interfaceName,
            string returnType,
            bool returnsVoid,
            ImmutableArray<ParameterModel> parameters,
            bool runOnCapturedContext)
        {
            Namespace = @namespace;
            Accessibility = accessibility;
            ClassName = className;
            InterfaceName = interfaceName;
            ReturnType = returnType;
            ReturnsVoid = returnsVoid;
            Parameters = parameters;
            RunOnCapturedContext =
                runOnCapturedContext;
        }

        public string? Namespace { get; }

        public string Accessibility { get; }

        public string ClassName { get; }

        public string InterfaceName { get; }

        public string ReturnType { get; }

        public bool ReturnsVoid { get; }

        public ImmutableArray<ParameterModel>
            Parameters
        { get; }

        public bool RunOnCapturedContext { get; }
    }

    private sealed class ParameterModel
    {
        public ParameterModel(
            string name,
            string type,
            RefKind refKind,
            bool isParams)
        {
            Name = name;
            Type = type;
            RefKind = refKind;
            IsParams = isParams;
        }

        public string Name { get; }

        public string Type { get; }

        public RefKind RefKind { get; }

        public bool IsParams { get; }
    }

    private sealed class GenerationResult
    {
        private GenerationResult(
            ListenerModel? model,
            Diagnostic? diagnostic)
        {
            Model = model;
            Diagnostic = diagnostic;
        }

        public static GenerationResult Empty { get; } =
            new(null, null);

        public ListenerModel? Model { get; }

        public Diagnostic? Diagnostic { get; }

        public static GenerationResult FromModel(
            ListenerModel model)
        {
            return new GenerationResult(
                model,
                null);
        }

        public static GenerationResult FromDiagnostic(
            Diagnostic diagnostic)
        {
            return new GenerationResult(
                null,
                diagnostic);
        }
    }

    private static class Diagnostics
    {
        private const string Category =
            "MatrixListenerGenerator";

        public static readonly DiagnosticDescriptor
            ClassMustBePartial = Create(
                "FENNEC001",
                "Listener class must be partial",
                "Listener class '{0}' must be declared partial");

        public static readonly DiagnosticDescriptor
            TargetMustBeClass = Create(
                "FENNEC002",
                "Listener target must be a class",
                "'{0}' must be declared as a class");

        public static readonly DiagnosticDescriptor
            ClassCannotBeStatic = Create(
                "FENNEC003",
                "Listener class cannot be static",
                "Listener class '{0}' cannot be static");

        public static readonly DiagnosticDescriptor
            ListenerMustBeInterface = Create(
                "FENNEC004",
                "Listener type must be an interface",
                "Listener type '{0}' must be an interface");

        public static readonly DiagnosticDescriptor
            OnUpdateNotFound = Create(
                "FENNEC005",
                "OnUpdate method not found",
                "Listener interface '{0}' does not define OnUpdate");

        public static readonly DiagnosticDescriptor
            MultipleOnUpdateMethods = Create(
                "FENNEC006",
                "Multiple OnUpdate methods found",
                "Listener interface '{0}' defines multiple OnUpdate methods");

        public static readonly DiagnosticDescriptor
            GenericOnUpdateNotSupported = Create(
                "FENNEC007",
                "Generic OnUpdate is unsupported",
                "Listener interface '{0}' has a generic OnUpdate method");

        public static readonly DiagnosticDescriptor
            GenericTargetNotSupported = Create(
                "FENNEC008",
                "Generic listener class is unsupported",
                "Generated listener class '{0}' cannot itself be generic");

        public static readonly DiagnosticDescriptor
            NestedTargetNotSupported = Create(
                "FENNEC009",
                "Nested listener class is unsupported",
                "Generated listener class '{0}' cannot be nested");

        public static readonly DiagnosticDescriptor
            AdditionalInterfaceMembers = Create(
                "FENNEC010",
                "Listener has additional members",
                "Listener interface '{0}' must expose only one ordinary OnUpdate method");

        public static readonly DiagnosticDescriptor
            ContextDispatchRequiresVoid = Create(
                "FENNEC011",
                "Context dispatch requires a void callback",
                "Listener interface '{0}' returns '{1}'. Set RunOnCapturedContext = false or use a void listener.");

        public static readonly DiagnosticDescriptor
            ContextDispatchDoesNotSupportRefParameters =
                Create(
                    "FENNEC012",
                    "Context dispatch cannot capture ref parameters",
                    "Listener interface '{0}' uses ref, in, or out parameters. Set RunOnCapturedContext = false.");

        public static readonly DiagnosticDescriptor
            AbstractTargetNotSupported = Create(
                "FENNEC013",
                "Listener class cannot be abstract",
                "Generated listener class '{0}' cannot be abstract");

        public static readonly DiagnosticDescriptor
            StaticOnUpdateNotSupported = Create(
                "FENNEC014",
                "Static OnUpdate is unsupported",
                "Listener interface '{0}' has a static OnUpdate method");

        private static DiagnosticDescriptor Create(
            string id,
            string title,
            string messageFormat)
        {
            return new DiagnosticDescriptor(
                id,
                title,
                messageFormat,
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true);
        }
    }
}
