// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// Reports CsWin32 <c>HRESULT</c> values that a caller ignores on a normal path.
/// </summary>
/// <remarks>
/// <para>
/// Each invocation that returns the CsWin32 <c>HRESULT</c> creates an obligation.
/// Any explicit use of the value satisfies the obligation: for example a check, a comparison, a switch,
/// a discard assignment, an argument, a store to a field or collection, or a return.
/// Reading <c>Value</c> or converting the value moves the obligation to the result.
/// </para>
/// <para>
/// The analyzer follows the value through locals and parameters on the control flow graph.
/// It reports the invocation when the value is dropped, when a local that holds it is overwritten before a read,
/// or when the value is still unchecked on some path that leaves the local's scope or the method normally.
/// Exception paths do not cause reports.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HResultAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Reports an HRESULT that is not checked, compared, passed on, stored, returned, or explicitly discarded.
    /// </summary>
    public static readonly DiagnosticDescriptor HResultMustBeChecked = new(
        "PInvoke019",
        "Do not ignore HRESULT results",
        "Check the HRESULT returned by '{0}' on every path, or discard it with '_ ='",
        "Reliability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private const string GeneratedCodeAttributeName = "System.CodeDom.Compiler.GeneratedCodeAttribute";
    private const string CsWin32ToolName = "Microsoft.Windows.CsWin32";

    /// <summary>
    /// Describes how an expression uses the value that flows into it.
    /// </summary>
    private enum ValueUse
    {
        /// <summary>The value is used, so the obligation is satisfied.</summary>
        Consumed,

        /// <summary>The value is dropped.</summary>
        Ignored,

        /// <summary>The value moves into a tracked local, parameter, or flow capture.</summary>
        Transferred,

        /// <summary>The compiler reads the value without a user-visible use.</summary>
        Neutral,
    }

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(HResultMustBeChecked);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            ConcurrentDictionary<ITypeSymbol, bool> hresultTypes = new(SymbolEqualityComparer.Default);
            bool IsHResult(ITypeSymbol? type) => type is not null && hresultTypes.GetOrAdd(type, IsCsWin32HResult);
            startContext.RegisterOperationBlockAction(blockContext => AnalyzeOperationBlocks(blockContext, IsHResult));
        });
    }

    private static bool IsCsWin32HResult(ITypeSymbol type) => type is INamedTypeSymbol { Name: "HRESULT", TypeKind: TypeKind.Struct }
        && type.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == GeneratedCodeAttributeName
            && attribute.ConstructorArguments.Length > 0
            && attribute.ConstructorArguments[0].Value is CsWin32ToolName);

    private static bool IsNullable(ITypeSymbol? type) => type?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static bool IsOrigin(IOperation operation, Func<ITypeSymbol?, bool> isHResult) => operation switch
    {
        IInvocationOperation { TargetMethod: { ReturnsByRef: false, ReturnsByRefReadonly: false } method } =>
            isHResult(method.ReturnType) && !(method.Name == "ThrowOnFailure" && isHResult(method.ContainingType)),
        IFunctionPointerInvocationOperation pointer => isHResult(pointer.Type),
        _ => false,
    };

    private static string GetOriginName(IOperation origin) => origin switch
    {
        IInvocationOperation { TargetMethod.MethodKind: MethodKind.DelegateInvoke, Instance: { } target } => target.Syntax.ToString(),
        IInvocationOperation invocation => invocation.TargetMethod.Name,
        IFunctionPointerInvocationOperation pointer => pointer.Target.Syntax.ToString(),
        _ => origin.Syntax.ToString(),
    };

    private static void AnalyzeOperationBlocks(OperationBlockAnalysisContext context, Func<ITypeSymbol?, bool> isHResult)
    {
        HashSet<IOperation> analyzedRoots = new();
        HashSet<SyntaxNode> reported = new();
        foreach (IOperation block in context.OperationBlocks)
        {
            IOperation root = block;
            while (root.Parent is not null)
            {
                root = root.Parent;
            }

            if (!analyzedRoots.Add(root)
                || root.Kind is not (OperationKind.Block or OperationKind.MethodBody or OperationKind.ConstructorBody or OperationKind.FieldInitializer or OperationKind.PropertyInitializer or OperationKind.ParameterInitializer)
                || !root.DescendantsAndSelf().Any(operation => IsOrigin(operation, isHResult)))
            {
                continue;
            }

            SharedState shared = new(context, isHResult, FindEscapingVariables(root, context.OwningSymbol), reported);
            new GraphAnalysis(context.GetControlFlowGraph(block), context.OwningSymbol, shared).Run();
        }
    }

    /// <summary>
    /// Finds locals and parameters that the analysis must not track, because code outside the current function
    /// can read them, or because a reference or pointer aliases them.
    /// </summary>
    private static HashSet<ISymbol> FindEscapingVariables(IOperation root, ISymbol owner)
    {
        HashSet<ISymbol> escaping = new(SymbolEqualityComparer.Default);
        foreach (IOperation operation in root.DescendantsAndSelf())
        {
            ISymbol? variable = operation switch
            {
                ILocalReferenceOperation local => local.Local,
                IParameterReferenceOperation parameter => parameter.Parameter,
                _ => null,
            };

            if (variable is null || escaping.Contains(variable))
            {
                continue;
            }

            ISymbol function = GetContainingFunction(operation, owner);
            if (!SymbolEqualityComparer.Default.Equals(variable.ContainingSymbol, function) || IsAliased(operation, function))
            {
                escaping.Add(variable);
            }
        }

        return escaping;
    }

    private static ISymbol GetContainingFunction(IOperation operation, ISymbol owner)
    {
        for (IOperation? current = operation.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IAnonymousFunctionOperation lambda:
                    return lambda.Symbol;
                case ILocalFunctionOperation localFunction:
                    return localFunction.Symbol;
            }
        }

        return owner;
    }

    private static bool IsAliased(IOperation reference, ISymbol function) => reference.Parent switch
    {
        IAddressOfOperation => true,
        ISimpleAssignmentOperation { IsRef: true } assignment => assignment.Value == reference,
        IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None } } => true,
        IConditionalOperation { IsRef: true } => true,
        IReturnOperation => function is IMethodSymbol { RefKind: not RefKind.None },
        _ => false,
    };

    private static ImmutableDictionary<object, ImmutableHashSet<IOperation>> Merge(ImmutableDictionary<object, ImmutableHashSet<IOperation>> left, ImmutableDictionary<object, ImmutableHashSet<IOperation>> right)
    {
        if (left.IsEmpty)
        {
            return right;
        }

        if (right.IsEmpty)
        {
            return left;
        }

        ImmutableDictionary<object, ImmutableHashSet<IOperation>>.Builder builder = left.ToBuilder();
        foreach (KeyValuePair<object, ImmutableHashSet<IOperation>> pair in right)
        {
            builder[pair.Key] = builder.TryGetValue(pair.Key, out ImmutableHashSet<IOperation>? existing) ? existing.Union(pair.Value) : pair.Value;
        }

        return builder.ToImmutable();
    }

    private static bool AreEqual(ImmutableDictionary<object, ImmutableHashSet<IOperation>> left, ImmutableDictionary<object, ImmutableHashSet<IOperation>> right)
        => left == right || (left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out ImmutableHashSet<IOperation>? other) && other.SetEquals(pair.Value)));

    /// <summary>
    /// Compares tracked variables: locals and parameters by symbol, and flow captures by capture ID.
    /// </summary>
    private sealed class VariableComparer : IEqualityComparer<object>
    {
        /// <summary>
        /// The only instance of this comparer.
        /// </summary>
        internal static readonly VariableComparer Instance = new();

        /// <inheritdoc/>
        public new bool Equals(object? x, object? y) => x is ISymbol left && y is ISymbol right ? SymbolEqualityComparer.Default.Equals(left, right) : object.Equals(x, y);

        /// <inheritdoc/>
        public int GetHashCode(object obj) => obj is ISymbol symbol ? SymbolEqualityComparer.Default.GetHashCode(symbol) : obj.GetHashCode();
    }

    /// <summary>
    /// Holds the data that all control flow graphs of one operation block share.
    /// </summary>
    private sealed class SharedState
    {
        private readonly OperationBlockAnalysisContext context;
        private readonly HashSet<SyntaxNode> reported;

        /// <summary>
        /// Initializes a new instance of the <see cref="SharedState"/> class.
        /// </summary>
        /// <param name="context">The operation block context that receives diagnostics.</param>
        /// <param name="isHResult">Tests whether a type is the CsWin32 HRESULT.</param>
        /// <param name="escapingVariables">The locals and parameters that the analysis must not track.</param>
        /// <param name="reported">The invocations that already have a diagnostic.</param>
        internal SharedState(OperationBlockAnalysisContext context, Func<ITypeSymbol?, bool> isHResult, HashSet<ISymbol> escapingVariables, HashSet<SyntaxNode> reported)
        {
            this.context = context;
            this.IsHResult = isHResult;
            this.EscapingVariables = escapingVariables;
            this.reported = reported;
        }

        /// <summary>
        /// Gets the function that tests whether a type is the CsWin32 HRESULT.
        /// </summary>
        internal Func<ITypeSymbol?, bool> IsHResult { get; }

        /// <summary>
        /// Gets the locals and parameters that the analysis must not track.
        /// </summary>
        internal HashSet<ISymbol> EscapingVariables { get; }

        /// <summary>
        /// Gets the cancellation token for the analysis.
        /// </summary>
        internal CancellationToken CancellationToken => this.context.CancellationToken;

        /// <summary>
        /// Reports an invocation once.
        /// </summary>
        /// <param name="origin">The invocation whose HRESULT is not checked.</param>
        internal void Report(IOperation origin)
        {
            if (this.reported.Add(origin.Syntax))
            {
                this.context.ReportDiagnostic(Diagnostic.Create(HResultMustBeChecked, origin.Syntax.GetLocation(), GetOriginName(origin)));
            }
        }
    }

    /// <summary>
    /// Runs a forward "may be unchecked" data flow analysis on one control flow graph.
    /// </summary>
    /// <remarks>
    /// The state maps each tracked variable to the invocations whose HRESULT it may hold unchecked.
    /// The analysis joins states with a union.
    /// Assignment-target captures refer to storage locations, so reads and writes through them use the underlying variable.
    /// </remarks>
    private sealed class GraphAnalysis
    {
        private static readonly ImmutableDictionary<object, ImmutableHashSet<IOperation>> EmptyState = ImmutableDictionary.Create<object, ImmutableHashSet<IOperation>>(VariableComparer.Instance);

        private readonly ControlFlowGraph graph;
        private readonly ISymbol owner;
        private readonly SharedState shared;
        private readonly Dictionary<CaptureId, IOperation> capturedTargets = new();
        private readonly HashSet<ControlFlowRegion> analyzedFinallyRegions = new();
        private readonly Dictionary<ControlFlowRegion, List<(ImmutableDictionary<object, ImmutableHashSet<IOperation>> Input, ImmutableDictionary<object, ImmutableHashSet<IOperation>> Output)>> finallyResults = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphAnalysis"/> class.
        /// </summary>
        /// <param name="graph">The control flow graph to analyze.</param>
        /// <param name="owner">The method, local function, or lambda that the graph represents.</param>
        /// <param name="shared">The data that all graphs of the operation block share.</param>
        internal GraphAnalysis(ControlFlowGraph graph, ISymbol owner, SharedState shared)
        {
            this.graph = graph;
            this.owner = owner;
            this.shared = shared;

            IOperation[] operations = graph.Blocks
                .SelectMany(block => block.BranchValue is null ? block.Operations : block.Operations.Add(block.BranchValue))
                .SelectMany(operation => operation.DescendantsAndSelf())
                .ToArray();
            HashSet<CaptureId> targetCaptures = new(operations
                .OfType<IAssignmentOperation>()
                .Select(assignment => assignment.Target)
                .OfType<IFlowCaptureReferenceOperation>()
                .Select(capture => capture.Id));
            foreach (IFlowCaptureOperation capture in operations.OfType<IFlowCaptureOperation>())
            {
                if (targetCaptures.Contains(capture.Id))
                {
                    this.capturedTargets[capture.Id] = capture.Value;
                }
            }
        }

        /// <summary>
        /// Analyzes the graph and the graphs of its local functions and lambdas.
        /// </summary>
        internal void Run()
        {
            if (!this.graph.OriginalOperation.DescendantsAndSelf().Any(this.IsOrigin))
            {
                return;
            }

            ImmutableDictionary<object, ImmutableHashSet<IOperation>> exit = this.RunRegion(this.graph.Root, EmptyState);
            foreach (KeyValuePair<object, ImmutableHashSet<IOperation>> pair in exit)
            {
                if (pair.Key is ISymbol)
                {
                    this.ReportAll(pair.Value);
                }
            }

            // Analyze finally regions that only exception paths reach, so that dropped values in them get a report.
            foreach (ControlFlowRegion region in EnumerateRegions(this.graph.Root))
            {
                if (region.Kind == ControlFlowRegionKind.Finally && !this.analyzedFinallyRegions.Contains(region) && this.graph.Blocks[region.FirstBlockOrdinal].IsReachable)
                {
                    this.RunFinally(region, EmptyState);
                }
            }

            foreach (IMethodSymbol localFunction in this.graph.LocalFunctions)
            {
                new GraphAnalysis(this.graph.GetLocalFunctionControlFlowGraph(localFunction, this.shared.CancellationToken), localFunction, this.shared).Run();
            }

            foreach (BasicBlock block in this.graph.Blocks)
            {
                foreach (IOperation operation in block.BranchValue is null ? block.Operations : block.Operations.Add(block.BranchValue))
                {
                    foreach (IFlowAnonymousFunctionOperation lambda in operation.DescendantsAndSelf().OfType<IFlowAnonymousFunctionOperation>())
                    {
                        new GraphAnalysis(this.graph.GetAnonymousFunctionControlFlowGraph(lambda, this.shared.CancellationToken), lambda.Symbol, this.shared).Run();
                    }
                }
            }
        }

        /// <summary>
        /// Gets the variable that an operation reads or writes, when the analysis tracks it.
        /// </summary>
        /// <param name="operation">A local, parameter, or flow capture reference.</param>
        /// <param name="variable">Receives the symbol or capture ID.</param>
        /// <returns><see langword="true"/> if the analysis tracks the variable.</returns>
        internal bool TryGetVariable(IOperation operation, [NotNullWhen(true)] out object? variable)
        {
            if (operation is IFlowCaptureReferenceOperation captureReference
                && this.capturedTargets.TryGetValue(captureReference.Id, out IOperation? target))
            {
                return this.TryGetVariable(target, out variable);
            }

            variable = operation switch
            {
                ILocalReferenceOperation local when this.IsTracked(local.Local) => local.Local,
                IParameterReferenceOperation parameter when this.IsTracked(parameter.Parameter) => parameter.Parameter,
                IFlowCaptureReferenceOperation capture => capture.Id,
                _ => null,
            };
            return variable is not null;
        }

        /// <summary>
        /// Tests whether a capture holds an assignment target rather than its current value.
        /// </summary>
        /// <param name="capture">The capture ID.</param>
        /// <returns><see langword="true"/> if the capture identifies an assignment target.</returns>
        internal bool IsCapturedTarget(CaptureId capture) => this.capturedTargets.ContainsKey(capture);

        /// <summary>
        /// Tests whether the analysis tracks a local or parameter.
        /// </summary>
        /// <param name="symbol">The local or parameter.</param>
        /// <returns><see langword="true"/> if only this function can read or write the variable.</returns>
        internal bool IsTracked(ISymbol symbol)
        {
            bool trackableKind = symbol switch
            {
                ILocalSymbol local => local.RefKind == RefKind.None && !local.IsConst,
                IParameterSymbol parameter => parameter.RefKind == RefKind.None,
                _ => false,
            };

            return trackableKind
                && SymbolEqualityComparer.Default.Equals(symbol.ContainingSymbol, this.owner)
                && !this.shared.EscapingVariables.Contains(symbol);
        }

        /// <summary>
        /// Tests whether an operation is an invocation that returns the CsWin32 HRESULT.
        /// </summary>
        /// <param name="operation">The operation to test.</param>
        /// <returns><see langword="true"/> if the operation creates an obligation to use its result.</returns>
        internal bool IsOrigin(IOperation operation) => HResultAnalyzer.IsOrigin(operation, this.shared.IsHResult);

        /// <summary>
        /// Reports each invocation in a set.
        /// </summary>
        /// <param name="origins">The invocations whose HRESULT is not checked.</param>
        internal void ReportAll(IEnumerable<IOperation> origins)
        {
            foreach (IOperation origin in origins)
            {
                this.shared.Report(origin);
            }
        }

        /// <summary>
        /// Finds how the expression that contains an operation uses the operation's value.
        /// </summary>
        /// <param name="operation">An HRESULT invocation, or a read of a tracked variable.</param>
        /// <param name="destination">Receives the assignment or flow capture that receives a transferred value.</param>
        /// <returns>The use of the value.</returns>
        internal ValueUse Classify(IOperation operation, out IOperation? destination)
        {
            destination = null;
            IOperation current = operation;
            while (true)
            {
                switch (current.Parent)
                {
                    case null:
                        return ValueUse.Consumed;
                    case IConversionOperation:
                    case IParenthesizedOperation:
                        break;
                    case IFieldReferenceOperation field when field.Instance == current && field.Field.Name == "Value" && this.shared.IsHResult(field.Field.ContainingType):
                        break;
                    case IPropertyReferenceOperation property when property.Instance == current && property.Property.Name == "Value" && IsNullable(property.Property.ContainingType):
                        break;
                    case IInvocationOperation invocation when invocation.Instance == current && invocation.TargetMethod.Name == "GetValueOrDefault" && IsNullable(invocation.TargetMethod.ContainingType):
                        break;
                    case IExpressionStatementOperation:
                        return ValueUse.Ignored;
                    case IIsNullOperation:
                        return ValueUse.Neutral;
                    case ISimpleAssignmentOperation assignment when assignment.Value == current:
                        if (assignment.IsRef || !this.TryGetVariable(assignment.Target, out _))
                        {
                            return ValueUse.Consumed;
                        }

                        if (assignment.Parent is null or IExpressionStatementOperation)
                        {
                            destination = assignment;
                            return ValueUse.Transferred;
                        }

                        // The assignment result is also used, so the obligation follows that use.
                        break;
                    case IFlowCaptureOperation capture:
                        destination = capture;
                        return ValueUse.Transferred;
                    default:
                        return ValueUse.Consumed;
                }

                current = current.Parent;
            }
        }

        private static IEnumerable<ControlFlowRegion> EnumerateRegions(ControlFlowRegion region)
        {
            yield return region;
            foreach (ControlFlowRegion nested in region.NestedRegions)
            {
                foreach (ControlFlowRegion descendant in EnumerateRegions(nested))
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>
        /// Finds the catch and filter regions that start an exception handler inside a scope, but not inside a nested finally region.
        /// </summary>
        private static IEnumerable<ControlFlowRegion> GetHandlerRegions(ControlFlowRegion scope)
        {
            foreach (ControlFlowRegion nested in scope.NestedRegions)
            {
                if (nested.Kind == ControlFlowRegionKind.Finally)
                {
                    continue;
                }

                if (scope.Kind == ControlFlowRegionKind.TryAndCatch && nested.Kind != ControlFlowRegionKind.Try)
                {
                    yield return nested;
                }

                foreach (ControlFlowRegion handler in GetHandlerRegions(nested))
                {
                    yield return handler;
                }
            }
        }

        private static ControlFlowRegion? GetNearestFinallyOrFilter(BasicBlock block)
        {
            ControlFlowRegion? region = block.EnclosingRegion;
            while (region is not null and not { Kind: ControlFlowRegionKind.Finally or ControlFlowRegionKind.Filter })
            {
                region = region.EnclosingRegion;
            }

            return region;
        }

        /// <summary>
        /// Runs the analysis on the blocks of a root or finally region.
        /// </summary>
        /// <param name="scope">The root region, or a finally region.</param>
        /// <param name="entry">The state when control enters the region.</param>
        /// <returns>The state at the exit block for the root region, or the state at the end of a finally region.</returns>
        /// <remarks>
        /// Exception handlers start with an empty state, so values that an exception interrupts do not cause reports.
        /// </remarks>
        private ImmutableDictionary<object, ImmutableHashSet<IOperation>> RunRegion(ControlFlowRegion scope, ImmutableDictionary<object, ImmutableHashSet<IOperation>> entry)
        {
            Dictionary<int, ImmutableDictionary<object, ImmutableHashSet<IOperation>>> inputs = new();
            Queue<int> worklist = new();
            HashSet<int> queued = new();
            ImmutableDictionary<object, ImmutableHashSet<IOperation>> end = EmptyState;

            Enqueue(scope.FirstBlockOrdinal, entry);
            foreach (ControlFlowRegion handler in GetHandlerRegions(scope))
            {
                if (this.graph.Blocks[handler.FirstBlockOrdinal].IsReachable)
                {
                    Enqueue(handler.FirstBlockOrdinal, EmptyState);
                }
            }

            while (worklist.Count > 0)
            {
                this.shared.CancellationToken.ThrowIfCancellationRequested();
                int ordinal = worklist.Dequeue();
                queued.Remove(ordinal);
                BasicBlock block = this.graph.Blocks[ordinal];
                if (block.Kind == BasicBlockKind.Exit)
                {
                    continue;
                }

                BlockWalker walker = new(this, inputs[ordinal]);
                foreach (IOperation operation in block.Operations)
                {
                    walker.Visit(operation);
                }

                if (block.BranchValue is not null)
                {
                    walker.Visit(block.BranchValue);
                }

                ImmutableDictionary<object, ImmutableHashSet<IOperation>> output = walker.GetState();
                Follow(block.ConditionalSuccessor, output);
                Follow(block.FallThroughSuccessor, output);
            }

            if (scope.Kind != ControlFlowRegionKind.Root)
            {
                return end;
            }

            return inputs.TryGetValue(this.graph.Blocks.Length - 1, out ImmutableDictionary<object, ImmutableHashSet<IOperation>>? exit) ? exit : EmptyState;

            void Enqueue(int ordinal, ImmutableDictionary<object, ImmutableHashSet<IOperation>> state)
            {
                if (inputs.TryGetValue(ordinal, out ImmutableDictionary<object, ImmutableHashSet<IOperation>>? existing))
                {
                    state = Merge(existing, state);
                    if (AreEqual(state, existing))
                    {
                        return;
                    }
                }

                inputs[ordinal] = state;
                if (queued.Add(ordinal))
                {
                    worklist.Enqueue(ordinal);
                }
            }

            void Follow(ControlFlowBranch? branch, ImmutableDictionary<object, ImmutableHashSet<IOperation>> state)
            {
                switch (branch?.Semantics)
                {
                    case ControlFlowBranchSemantics.Regular or ControlFlowBranchSemantics.Return when branch.Destination is not null:
                        foreach (ControlFlowRegion finallyRegion in branch.FinallyRegions)
                        {
                            state = this.RunFinally(finallyRegion, state);
                        }

                        Enqueue(branch.Destination.Ordinal, this.LeaveRegions(branch.LeavingRegions, state));
                        break;
                    case ControlFlowBranchSemantics.StructuredExceptionHandling when scope.Kind == ControlFlowRegionKind.Finally && GetNearestFinallyOrFilter(branch.Source) == scope:
                        end = Merge(end, state);
                        break;
                }
            }
        }

        private ImmutableDictionary<object, ImmutableHashSet<IOperation>> RunFinally(ControlFlowRegion region, ImmutableDictionary<object, ImmutableHashSet<IOperation>> input)
        {
            this.analyzedFinallyRegions.Add(region);
            if (!this.finallyResults.TryGetValue(region, out List<(ImmutableDictionary<object, ImmutableHashSet<IOperation>> Input, ImmutableDictionary<object, ImmutableHashSet<IOperation>> Output)>? results))
            {
                this.finallyResults.Add(region, results = new());
            }

            foreach ((ImmutableDictionary<object, ImmutableHashSet<IOperation>> Input, ImmutableDictionary<object, ImmutableHashSet<IOperation>> Output) result in results)
            {
                if (AreEqual(result.Input, input))
                {
                    return result.Output;
                }
            }

            ImmutableDictionary<object, ImmutableHashSet<IOperation>> output = this.RunRegion(region, input);
            results.Add((input, output));
            return output;
        }

        /// <summary>
        /// Reports unchecked values in locals that go out of scope, and forgets the locals and flow captures of the regions.
        /// </summary>
        private ImmutableDictionary<object, ImmutableHashSet<IOperation>> LeaveRegions(ImmutableArray<ControlFlowRegion> regions, ImmutableDictionary<object, ImmutableHashSet<IOperation>> state)
        {
            foreach (ControlFlowRegion region in regions)
            {
                foreach (ILocalSymbol local in region.Locals)
                {
                    if (state.TryGetValue(local, out ImmutableHashSet<IOperation>? origins))
                    {
                        this.ReportAll(origins);
                        state = state.Remove(local);
                    }
                }

                foreach (CaptureId capture in region.CaptureIds)
                {
                    state = state.Remove(capture);
                }
            }

            return state;
        }
    }

    /// <summary>
    /// Applies the operations of one basic block to the analysis state, in evaluation order.
    /// </summary>
    private sealed class BlockWalker
    {
        private readonly GraphAnalysis analysis;
        private readonly ImmutableDictionary<object, ImmutableHashSet<IOperation>>.Builder state;
        private readonly Dictionary<IOperation, ImmutableHashSet<IOperation>> inflow = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="BlockWalker"/> class.
        /// </summary>
        /// <param name="analysis">The graph analysis that owns this walker.</param>
        /// <param name="input">The state at the start of the block.</param>
        internal BlockWalker(GraphAnalysis analysis, ImmutableDictionary<object, ImmutableHashSet<IOperation>> input)
        {
            this.analysis = analysis;
            this.state = input.ToBuilder();
        }

        /// <summary>
        /// Gets the state after the operations that the walker visited.
        /// </summary>
        /// <returns>The current state.</returns>
        internal ImmutableDictionary<object, ImmutableHashSet<IOperation>> GetState() => this.state.ToImmutable();

        /// <summary>
        /// Applies an operation and its children to the state.
        /// </summary>
        /// <param name="operation">The operation to apply.</param>
        internal void Visit(IOperation operation)
        {
            object? variable;
            switch (operation)
            {
                case IAnonymousFunctionOperation or IFlowAnonymousFunctionOperation or ILocalFunctionOperation:
                    // Each function has a separate graph.
                    return;
                case ISimpleAssignmentOperation { IsRef: false } assignment when this.analysis.TryGetVariable(assignment.Target, out variable):
                    this.Visit(assignment.Value);
                    this.Write(variable, this.TakeInflow(assignment), reportOverwrite: true);
                    return;
                case IFlowCaptureOperation capture:
                    if (this.analysis.IsCapturedTarget(capture.Id))
                    {
                        // Capturing a local's storage location does not read or overwrite its current value.
                        if (!this.analysis.TryGetVariable(capture.Value, out _))
                        {
                            this.Visit(capture.Value);
                        }

                        return;
                    }

                    this.Visit(capture.Value);
                    this.Write(capture.Id, this.TakeInflow(capture), reportOverwrite: false);
                    return;
                case ICompoundAssignmentOperation compound when this.analysis.TryGetVariable(compound.Target, out variable):
                    this.Visit(compound.Value);
                    this.state.Remove(variable);
                    return;
                case IIncrementOrDecrementOperation increment when this.analysis.TryGetVariable(increment.Target, out variable):
                    this.state.Remove(variable);
                    return;
                case ILocalReferenceOperation or IParameterReferenceOperation or IFlowCaptureReferenceOperation:
                    if (this.analysis.TryGetVariable(operation, out variable))
                    {
                        this.Read(operation, variable);
                    }

                    return;
                case IDeconstructionAssignmentOperation deconstruction:
                    List<object> targets = new();
                    this.VisitDeconstructionTarget(deconstruction.Target, targets);
                    this.Visit(deconstruction.Value);
                    foreach (object target in targets)
                    {
                        this.Write(target, ImmutableHashSet<IOperation>.Empty, reportOverwrite: true);
                    }

                    return;
                case IDeclarationPatternOperation { DeclaredSymbol: ILocalSymbol local } when this.analysis.IsTracked(local):
                    this.Write(local, ImmutableHashSet<IOperation>.Empty, reportOverwrite: true);
                    return;
                default:
                    List<object>? outputs = null;
                    foreach (IOperation child in operation.ChildOperations)
                    {
                        // A method writes its out arguments after the caller evaluates all arguments.
                        if (child is IArgumentOperation { Parameter.RefKind: RefKind.Out } argument
                            && this.analysis.TryGetVariable(argument.Value is IDeclarationExpressionOperation declaration ? declaration.Expression : argument.Value, out variable))
                        {
                            (outputs ??= new()).Add(variable);
                            continue;
                        }

                        this.Visit(child);
                    }

                    if (this.analysis.IsOrigin(operation))
                    {
                        this.Produce(operation);
                    }

                    if (outputs is not null)
                    {
                        foreach (object output in outputs)
                        {
                            this.Write(output, ImmutableHashSet<IOperation>.Empty, reportOverwrite: true);
                        }
                    }

                    return;
            }
        }

        private void VisitDeconstructionTarget(IOperation target, List<object> variables)
        {
            switch (target)
            {
                case IDeclarationExpressionOperation declaration:
                    this.VisitDeconstructionTarget(declaration.Expression, variables);
                    break;
                case ITupleOperation tuple:
                    foreach (IOperation element in tuple.Elements)
                    {
                        this.VisitDeconstructionTarget(element, variables);
                    }

                    break;
                default:
                    if (this.analysis.TryGetVariable(target, out object? variable))
                    {
                        variables.Add(variable);
                    }
                    else
                    {
                        this.Visit(target);
                    }

                    break;
            }
        }

        private void Produce(IOperation origin)
        {
            switch (this.analysis.Classify(origin, out IOperation? destination))
            {
                case ValueUse.Ignored:
                    this.analysis.ReportAll([origin]);
                    break;
                case ValueUse.Transferred:
                    this.AddInflow(destination!, ImmutableHashSet.Create(origin));
                    break;
            }
        }

        private void Read(IOperation reference, object variable)
        {
            if (!this.state.TryGetValue(variable, out ImmutableHashSet<IOperation>? origins))
            {
                return;
            }

            switch (this.analysis.Classify(reference, out IOperation? destination))
            {
                case ValueUse.Neutral:
                    return;
                case ValueUse.Ignored:
                    this.analysis.ReportAll(origins);
                    break;
                case ValueUse.Transferred:
                    this.AddInflow(destination!, origins);
                    break;
            }

            this.state.Remove(variable);
        }

        private void Write(object variable, ImmutableHashSet<IOperation> origins, bool reportOverwrite)
        {
            if (reportOverwrite && this.state.TryGetValue(variable, out ImmutableHashSet<IOperation>? pending))
            {
                this.analysis.ReportAll(pending);
            }

            if (origins.IsEmpty)
            {
                this.state.Remove(variable);
            }
            else
            {
                this.state[variable] = origins;
            }
        }

        private void AddInflow(IOperation destination, ImmutableHashSet<IOperation> origins)
        {
            this.inflow[destination] = this.inflow.TryGetValue(destination, out ImmutableHashSet<IOperation>? existing) ? existing.Union(origins) : origins;
        }

        private ImmutableHashSet<IOperation> TakeInflow(IOperation destination)
        {
            if (this.inflow.TryGetValue(destination, out ImmutableHashSet<IOperation>? origins))
            {
                this.inflow.Remove(destination);
                return origins;
            }

            return ImmutableHashSet<IOperation>.Empty;
        }
    }
}
