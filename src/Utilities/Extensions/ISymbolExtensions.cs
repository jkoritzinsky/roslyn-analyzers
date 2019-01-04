﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Analyzer.Utilities.Extensions
{
    internal static class ISymbolExtensions
    {
        public static bool IsType(this ISymbol symbol)
        {
            var typeSymbol = symbol as ITypeSymbol;
            return typeSymbol != null && typeSymbol.IsType;
        }

        public static bool IsAccessorMethod(this ISymbol symbol)
        {
            return symbol is IMethodSymbol accessorSymbol &&
                (accessorSymbol.IsPropertyAccessor() || accessorSymbol.IsEventAccessor());
        }

        public static IEnumerable<IMethodSymbol> GetAccessors(this ISymbol symbol)
        {
            switch (symbol.Kind)
            {
                case SymbolKind.Property:
                    var property = (IPropertySymbol)symbol;
                    if (property.GetMethod != null)
                    {
                        yield return property.GetMethod;
                    }

                    if (property.SetMethod != null)
                    {
                        yield return property.SetMethod;
                    }

                    break;

                case SymbolKind.Event:
                    var eventSymbol = (IEventSymbol)symbol;
                    if (eventSymbol.AddMethod != null)
                    {
                        yield return eventSymbol.AddMethod;
                    }

                    if (eventSymbol.RemoveMethod != null)
                    {
                        yield return eventSymbol.RemoveMethod;
                    }

                    if (eventSymbol.RaiseMethod != null)
                    {
                        yield return eventSymbol.RaiseMethod;
                    }

                    break;
            }
        }

        public static bool IsDefaultConstructor(this ISymbol symbol)
        {
            return symbol.IsConstructor() && symbol.GetParameters().Length == 0;
        }

        public static bool IsPublic(this ISymbol symbol)
        {
            return symbol.DeclaredAccessibility == Accessibility.Public;
        }

        public static bool IsProtected(this ISymbol symbol)
        {
            return symbol.DeclaredAccessibility == Accessibility.Protected;
        }

        public static bool IsPrivate(this ISymbol symbol)
        {
            return symbol.DeclaredAccessibility == Accessibility.Private;
        }

        public static bool IsErrorType(this ISymbol symbol)
        {
            return
                symbol is ITypeSymbol &&
                ((ITypeSymbol)symbol).TypeKind == TypeKind.Error;
        }

        public static bool IsConstructor(this ISymbol symbol)
        {
            return (symbol as IMethodSymbol)?.MethodKind == MethodKind.Constructor;
        }

        public static bool IsDestructor(this ISymbol symbol)
        {
            return (symbol as IMethodSymbol)?.IsFinalizer() ?? false;
        }

        public static bool IsIndexer(this ISymbol symbol)
        {
            return (symbol as IPropertySymbol)?.IsIndexer == true;
        }

        public static bool IsPropertyWithBackingField(this ISymbol symbol)
        {
            return symbol is IPropertySymbol propertySymbol &&
                propertySymbol.ContainingType.GetMembers().OfType<IFieldSymbol>().Any(f => f.IsImplicitlyDeclared && f.AssociatedSymbol == symbol);
        }

        public static bool IsUserDefinedOperator(this ISymbol symbol)
        {
            return (symbol as IMethodSymbol)?.MethodKind == MethodKind.UserDefinedOperator;
        }

        public static bool IsConversionOperator(this ISymbol symbol)
        {
            return (symbol as IMethodSymbol)?.MethodKind == MethodKind.Conversion;
        }

        public static ImmutableArray<IParameterSymbol> GetParameters(this ISymbol symbol)
        {
            return symbol.TypeSwitch(
                (IMethodSymbol m) => m.Parameters,
                (IPropertySymbol p) => p.Parameters,
                _ => ImmutableArray.Create<IParameterSymbol>());
        }

        /// <summary>
        /// Returns true if the given symbol has required visibility based on options:
        ///   1. If user has explicitly configured candidate <see cref="SymbolVisibilityGroup"/> in editor config options and
        ///      given symbol's visibility is one of the candidate visibilites.
        ///   2. Otherwise, if user has not configured visibility, and given symbol's visibility
        ///      matches the given default symbol visibility.
        /// </summary>
        public static bool MatchesConfiguredVisibility(
            this ISymbol symbol,
            AnalyzerOptions options,
            DiagnosticDescriptor rule,
            CancellationToken cancellationToken,
            SymbolVisibilityGroup defaultRequiredVisibility = SymbolVisibilityGroup.Public)
        {
            var allowedVisibilities = options.GetSymbolVisibilityGroupOption(rule, defaultRequiredVisibility, cancellationToken);
            return allowedVisibilities.Contains(symbol.GetResultantVisibility());
        }

        /// <summary>
        /// True if the symbol is externally visible outside this assembly.
        /// </summary>
        public static bool IsExternallyVisible(this ISymbol symbol) =>
            symbol.GetResultantVisibility() == SymbolVisibility.Public;

        public static SymbolVisibility GetResultantVisibility(this ISymbol symbol)
        {
            // Start by assuming it's visible.
            SymbolVisibility visibility = SymbolVisibility.Public;

            switch (symbol.Kind)
            {
                case SymbolKind.Alias:
                    // Aliases are uber private.  They're only visible in the same file that they
                    // were declared in.
                    return SymbolVisibility.Private;

                case SymbolKind.Parameter:
                    // Parameters are only as visible as their containing symbol
                    return GetResultantVisibility(symbol.ContainingSymbol);

                case SymbolKind.TypeParameter:
                    // Type Parameters are private.
                    return SymbolVisibility.Private;
            }

            while (symbol != null && symbol.Kind != SymbolKind.Namespace)
            {
                switch (symbol.DeclaredAccessibility)
                {
                    // If we see anything private, then the symbol is private.
                    case Accessibility.NotApplicable:
                    case Accessibility.Private:
                        return SymbolVisibility.Private;

                    // If we see anything internal, then knock it down from public to
                    // internal.
                    case Accessibility.Internal:
                    case Accessibility.ProtectedAndInternal:
                        visibility = SymbolVisibility.Internal;
                        break;

                        // For anything else (Public, Protected, ProtectedOrInternal), the
                        // symbol stays at the level we've gotten so far.
                }

                symbol = symbol.ContainingSymbol;
            }

            return visibility;
        }

        public static bool MatchMemberDerivedByName(this ISymbol member, INamedTypeSymbol type, string name)
        {
            return member != null && member.ContainingType.DerivesFrom(type) && member.MetadataName == name;
        }

        public static bool MatchMethodDerivedByName(this IMethodSymbol method, INamedTypeSymbol type, string name)
        {
            return method != null && method.MatchMemberDerivedByName(type, name);
        }

        public static bool MatchMethodByName(this ISymbol member, INamedTypeSymbol type, string name)
        {
            return member != null && member.Kind == SymbolKind.Method && member.MatchMemberByName(type, name);
        }

        public static bool MatchPropertyDerivedByName(this ISymbol member, INamedTypeSymbol type, string name)
        {
            return member != null && member.Kind == SymbolKind.Property && member.MatchMemberDerivedByName(type, name);
        }

        public static bool MatchFieldDerivedByName(this ISymbol member, INamedTypeSymbol type, string name)
        {
            return member != null && member.Kind == SymbolKind.Field && member.MatchMemberDerivedByName(type, name);
        }

        public static bool MatchMemberByName(this ISymbol member, INamedTypeSymbol type, string name)
        {
            return member != null && member.ContainingType == type && member.MetadataName == name;
        }

        public static bool MatchPropertyByName(this ISymbol member, INamedTypeSymbol type, string name)
        {
            return member != null && member.Kind == SymbolKind.Property && member.MatchMemberByName(type, name);
        }

        public static bool MatchFieldByName(this ISymbol member, INamedTypeSymbol type, string name)
        {
            return member != null && member.Kind == SymbolKind.Field && member.MatchMemberByName(type, name);
        }

        // Define the format in for displaying member names. The format is chosen to be consistent
        // consistent with FxCop's display format.
        private static readonly SymbolDisplayFormat s_memberDisplayFormat =
            // This format omits the namespace.
            SymbolDisplayFormat.CSharpShortErrorMessageFormat
                // Turn off the EscapeKeywordIdentifiers flag (which is on by default), so that
                // a method named "@for" is displayed as "for".
                // Turn on the UseSpecialTypes flat (which is off by default), so that parameter
                // names of "special" types such as Int32 are displayed as their language alias,
                // such as int for C# and Integer for VB.
                .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        /// <summary>
        /// Format member names in a way consistent with FxCop's display format.
        /// </summary>
        /// <param name="member"></param>
        /// <returns>
        /// A string representing the name of the member in a format consistent with FxCop.
        /// </returns>
        public static string FormatMemberName(this ISymbol member)
        {
            return member.ToDisplayString(s_memberDisplayFormat);
        }

        /// <summary>
        /// Check whether given parameters contains any parameter with given type.
        /// </summary>
        public static bool ContainsParameterOfType(this IEnumerable<IParameterSymbol> parameters, INamedTypeSymbol type)
        {
            var parametersOfType = GetParametersOfType(parameters, type);
            return parametersOfType.Any();
        }

        /// <summary>
        /// Get parameters which type is the given type
        /// </summary>
        public static IEnumerable<IParameterSymbol> GetParametersOfType(this IEnumerable<IParameterSymbol> parameters, INamedTypeSymbol type)
        {
            return parameters.Where(p => p.Type.Equals(type) == true);
        }

        /// <summary>
        /// Check whether given overloads has any overload whose parameters has the given type as its parameter type.
        /// </summary>
        public static bool HasOverloadWithParameterOfType(this IEnumerable<IMethodSymbol> overloads, IMethodSymbol self, INamedTypeSymbol type, CancellationToken cancellationToken)
        {
            foreach (var overload in overloads)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (self?.Equals(overload) == true)
                {
                    continue;
                }

                if (overload.Parameters.ContainsParameterOfType(type))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Convert given parameters to the indices to the given method's parameter list.
        /// </summary>
        public static IEnumerable<int> GetParameterIndices(this IMethodSymbol method, IEnumerable<IParameterSymbol> parameters, CancellationToken cancellationToken)
        {
            var set = new HashSet<IParameterSymbol>(parameters);
            for (var i = 0; i < method.Parameters.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (set.Contains(method.Parameters[i]))
                {
                    yield return i;
                }
            }
        }

        /// <summary>
        /// Check whether parameter types of the given methods are same for given parameter indices.
        /// </summary>
        public static bool ParameterTypesAreSame(this IMethodSymbol method1, IMethodSymbol method2, IEnumerable<int> parameterIndices, CancellationToken cancellationToken)
        {
            foreach (int index in parameterIndices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var type1 = method1.Parameters[index].Type.OriginalDefinition;
                var type2 = method2.Parameters[index].Type.OriginalDefinition;
                
                if (type1.TypeKind == TypeKind.TypeParameter &&
                    type2.TypeKind == TypeKind.TypeParameter &&
                    ((ITypeParameterSymbol)type1).Ordinal == ((ITypeParameterSymbol)type2).Ordinal)
                {
                    continue;
                }

                // this doesnt account for type conversion but FxCop implementation seems doesnt either
                // so this should match FxCop implementation.
                if (!type2.Equals(type1))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Check whether given symbol is from mscorlib
        /// </summary>
        public static bool IsFromMscorlib(this ISymbol symbol, Compilation compilation)
        {
            var @object = WellKnownTypes.Object(compilation);
            return symbol.ContainingAssembly?.Equals(@object.ContainingAssembly) == true;
        }

        /// <summary>
        /// Get overload from the given overloads that matches given method signature + given parameter
        /// </summary>
        public static IMethodSymbol GetMatchingOverload(this IMethodSymbol method, IEnumerable<IMethodSymbol> overloads, int parameterIndex, INamedTypeSymbol type, CancellationToken cancellationToken)
        {
            foreach (IMethodSymbol overload in overloads)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // does not account for method with optional parameters
                if (method.Equals(overload) || overload.Parameters.Length != method.Parameters.Length)
                {
                    // either itself, or signature is not same
                    continue;
                }

                if (!method.ParameterTypesAreSame(overload, Enumerable.Range(0, method.Parameters.Length).Where(i => i != parameterIndex), cancellationToken))
                {
                    // check whether remaining parameters match existing types, otherwise, we are not interested
                    continue;
                }

                if (overload.Parameters[parameterIndex].Type.Equals(type) == true)
                {
                    // we no longer interested in this overload. there can be only 1 match
                    return overload;
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if a given symbol implements an interface member implicitly or explicitly
        /// </summary>
        public static bool IsImplementationOfAnyInterfaceMember(this ISymbol symbol)
        {
            return symbol.IsImplementationOfAnyExplicitInterfaceMember() || symbol.IsImplementationOfAnyImplicitInterfaceMember();
        }

        public static bool IsImplementationOfAnyImplicitInterfaceMember(this ISymbol symbol)
        {
            return IsImplementationOfAnyImplicitInterfaceMember<ISymbol>(symbol);
        }

        /// <summary>
        /// Checks if a given symbol implements an interface member implicitly
        /// </summary>
        public static bool IsImplementationOfAnyImplicitInterfaceMember<TSymbol>(this ISymbol symbol)
        where TSymbol : ISymbol
        {
            if (symbol.ContainingType != null)
            {
                foreach (INamedTypeSymbol interfaceSymbol in symbol.ContainingType.AllInterfaces)
                {
                    foreach (var interfaceMember in interfaceSymbol.GetMembers().OfType<TSymbol>())
                    {
                        if (IsImplementationOfInterfaceMember(symbol, interfaceMember))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public static bool IsImplementationOfInterfaceMember(this ISymbol symbol, ISymbol interfaceMember)
        {
            return interfaceMember != null &&
                   symbol.Equals(symbol.ContainingType.FindImplementationForInterfaceMember(interfaceMember));
        }

        /// <summary>
        /// Checks if a given symbol implements an interface member or overrides an implementation of an interface member.
        /// </summary>
        public static bool IsOverrideOrImplementationOfInterfaceMember(this ISymbol symbol, ISymbol interfaceMember)
        {
            Debug.Assert(symbol != null);
            if (interfaceMember == null)
            {
                return false;
            }

            if (symbol.IsImplementationOfInterfaceMember(interfaceMember))
            {
                return true;
            }

            return symbol.IsOverride &&
                symbol.GetOverriddenMember().IsOverrideOrImplementationOfInterfaceMember(interfaceMember);
        }

        /// <summary>
        /// Gets the symbol overridden by the given <paramref name="symbol"/>.
        /// </summary>
        /// <remarks>Requires that <see cref="ISymbol.IsOverride"/> is true for the given <paramref name="symbol"/>.</remarks>
        public static ISymbol GetOverriddenMember(this ISymbol symbol)
        {
            Debug.Assert(symbol != null);
            Debug.Assert(symbol.IsOverride);

            switch(symbol)
            {
                case IMethodSymbol methodSymbol:
                    return methodSymbol.OverriddenMethod;

                case IPropertySymbol propertySymbol:
                    return propertySymbol.OverriddenProperty;

                case IEventSymbol eventSymbol:
                    return eventSymbol.OverriddenEvent;

                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Checks if a given symbol implements an interface member explicitly
        /// </summary>
        public static bool IsImplementationOfAnyExplicitInterfaceMember(this ISymbol symbol)
        {
            if (symbol is IMethodSymbol methodSymbol && methodSymbol.ExplicitInterfaceImplementations.Any())
            {
                return true;
            }

            if (symbol is IPropertySymbol propertySymbol && propertySymbol.ExplicitInterfaceImplementations.Any())
            {
                return true;
            }

            if (symbol is IEventSymbol eventSymbol && eventSymbol.ExplicitInterfaceImplementations.Any())
            {
                return true;
            }

            return false;
        }

        public static ITypeSymbol GetMemerOrLocalOrParameterType(this ISymbol symbol)
        {
            switch (symbol.Kind)
            {
                case SymbolKind.Event:
                    return ((IEventSymbol)symbol).Type;

                case SymbolKind.Field:
                    return ((IFieldSymbol)symbol).Type;

                case SymbolKind.Method:
                    return ((IMethodSymbol)symbol).ReturnType;

                case SymbolKind.Property:
                    return ((IPropertySymbol)symbol).Type;

                case SymbolKind.Local:
                    return ((ILocalSymbol)symbol).Type;

                case SymbolKind.Parameter:
                    return ((IParameterSymbol)symbol).Type;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Returns a value indicating whether the specified symbol has the specified
        /// attribute.
        /// </summary>
        /// <param name="symbol">
        /// The symbol being examined.
        /// </param>
        /// <param name="attribute">
        /// The attribute in question.
        /// </param>
        /// <returns>
        /// <c>true</c> if <paramref name="symbol"/> has an attribute of type
        /// <paramref name="attribute"/>; otherwise <c>false</c>.
        /// </returns>
        /// <remarks>
        /// If <paramref name="symbol"/> is a type, this method does not find attributes
        /// on its base types.
        /// </remarks>
        public static bool HasAttribute(this ISymbol symbol, INamedTypeSymbol attribute)
        {
            return symbol.GetAttributes().Any(attr => attr.AttributeClass.Equals(attribute));
        }
    }
}
