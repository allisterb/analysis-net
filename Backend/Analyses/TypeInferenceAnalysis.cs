// Copyright (c) Edgardo Zoppi. All Rights Reserved.
// See License.txt in the repository root directory for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Model.Types;
using Model.ThreeAddressCode.Instructions;
using Model.ThreeAddressCode.Visitor;
using Backend.Model;
using Model.ThreeAddressCode.Values;
using Backend.Utils;

namespace Backend.Analyses
{
	public class TypeInferenceAnalysis
	{
		#region class TypeInferer

		private class TypeInferer : InstructionVisitor
		{
			private IType returnType;

			public TypeInferer(IType returnType)
			{
				this.returnType = returnType;
			}

			public override void Visit(LocalAllocationInstruction instruction)
			{
				instruction.TargetAddress.Type = PlatformTypes.IntPtr;
			}

			public override void Visit(SizeofInstruction instruction)
			{
				instruction.Result.Type = PlatformTypes.SizeofType;
			}

			public override void Visit(CreateArrayInstruction instruction)
			{
				instruction.Result.Type = new ArrayType(instruction.ElementType, instruction.Rank);
			}

			public override void Visit(CatchInstruction instruction)
			{
				if (instruction.HasResult)
				{
					instruction.Result.Type = instruction.ExceptionType;
				}
			}

            public override void Visit(FilterInstruction instruction)
            {
                if (instruction.HasResult)
                {
                    instruction.Result.Type = instruction.ExceptionType;
                }
            }

			public override void Visit(CreateObjectInstruction instruction)
			{
				instruction.Result.Type = instruction.AllocationType;
			}

			public override void Visit(MethodCallInstruction instruction)
			{
				if (instruction.HasResult)
				{
					instruction.Result.Type = instruction.Method.ReturnType;
				}

				// Skip implicit "this" parameter.
				var offset = instruction.Method.IsStatic ? 0 : 1;

				for (var i = offset; i < instruction.Arguments.Count; ++i)
				{
					var argument = instruction.Arguments[i];
					var parameter = instruction.Method.Parameters[i - offset];

					// Set the null variable a type.
					if (argument.Type == null ||
						parameter.Type.Equals(PlatformTypes.Boolean))
					{
						argument.Type = parameter.Type;
					}
				}
			}

			public override void Visit(IndirectMethodCallInstruction instruction)
			{
				if (instruction.HasResult)
				{
					instruction.Result.Type = instruction.Function.ReturnType;
				}

				// Skip implicit "this" parameter.
				var offset = instruction.Function.IsStatic ? 0 : 1;

				for (var i = offset; i < instruction.Arguments.Count; ++i)
				{
					var argument = instruction.Arguments[i];
					var parameter = instruction.Function.Parameters[i - offset];

					// Set the null variable a type.
					if (argument.Type == null ||
						parameter.Type.Equals(PlatformTypes.Boolean))
					{
						argument.Type = parameter.Type;
					}
				}
			}

			public override void Visit(LoadInstruction instruction)
			{
				var operandAsConstant = instruction.Operand as Constant;
				var operandAsVariable = instruction.Operand as IVariable;

				// Null is a polymorphic value so we handle it specially. We don't set the
				// corresponding variable's type yet. We postpone it to usage of the variable
				// or set it to System.Object if it is never used.
				if (operandAsConstant != null)
				{
					if (operandAsConstant.Value == null)
					{
						instruction.Result.Type = PlatformTypes.Object;
					}
					else if (instruction.Result.Type != null &&
							 instruction.Result.Type.Equals(PlatformTypes.Boolean))
					{
						// If the result of the load has type Boolean,
						// then we are actually loading a Boolean constant.
						if (operandAsConstant.Value.Equals(0))
						{
							operandAsConstant.Value = false;
							operandAsConstant.Type = PlatformTypes.Boolean;
						}
						else if (operandAsConstant.Value.Equals(1))
						{
							operandAsConstant.Value = true;
							operandAsConstant.Type = PlatformTypes.Boolean;
						}
					}
				}
				// If we have variable to variable assignment where the result was assigned
				// a type but the operand was not, then we set the operand type accordingly.
				else if (operandAsVariable != null && 
						 instruction.Result.Type != null &&
						(operandAsVariable.Type == null ||
						 operandAsVariable.Type.Equals(PlatformTypes.Object) ||
						 instruction.Result.Type.Equals(PlatformTypes.Boolean)))
				{
					operandAsVariable.Type = instruction.Result.Type;
				}
				
				if (instruction.Result.Type == null)
				{
					instruction.Result.Type = instruction.Operand.Type;
				}
			}

			public override void Visit(LoadTokenInstruction instruction)
			{
				instruction.Result.Type = TypeHelper.TokenType(instruction.Token);
			}

			public override void Visit(StoreInstruction instruction)
			{
				// Set the null variable a type.
				if (instruction.Result.Type != null &&
				   (instruction.Operand.Type == null ||
					instruction.Operand.Type.Equals(PlatformTypes.Object) ||
					instruction.Result.Type.Equals(PlatformTypes.Boolean)))
				{
					instruction.Operand.Type = instruction.Result.Type;
				}
			}

			public override void Visit(ReturnInstruction instruction)
			{
				// Set the null variable a type.
				if (instruction.HasOperand &&
					returnType != null &&
				   (instruction.Operand.Type == null ||
					instruction.Operand.Type.Equals(PlatformTypes.Object) ||
					returnType.Equals(PlatformTypes.Boolean)))
				{
					instruction.Operand.Type = returnType;
				}
			}

			public override void Visit(UnaryInstruction instruction)
			{
				instruction.Result.Type = instruction.Operand.Type;
			}

			public override void Visit(ConvertInstruction instruction)
			{
				var type = instruction.Operand.Type;

				switch (instruction.Operation)
				{
					case ConvertOperation.Conv:
					case ConvertOperation.Cast:
					case ConvertOperation.Box:
					case ConvertOperation.Unbox:
						// ConversionType is the data type of the result.
						type = instruction.ConversionType;
						break;

					case ConvertOperation.UnboxPtr:
						// Pointer to ConversionType is the data type of the result.
						type = new PointerType(instruction.ConversionType);
						break;
				}

				instruction.Result.Type = type;
			}

			public override void Visit(PhiInstruction instruction)
			{
				var type = instruction.Arguments.First().Type;
				var arguments = instruction.Arguments.Skip(1);

				foreach (var argument in arguments)
				{
					type = TypeHelper.MergedType(type, argument.Type);
				}

				instruction.Result.Type = type;
			}

			public override void Visit(BinaryInstruction instruction)
			{
				var left = instruction.LeftOperand.Type;
				var right = instruction.RightOperand.Type;
				var unsigned = instruction.UnsignedOperands;

				switch (instruction.Operation)
				{
					case BinaryOperation.Add:
					case BinaryOperation.Div:
					case BinaryOperation.Mul:
					case BinaryOperation.Rem:
					case BinaryOperation.Sub:
						instruction.Result.Type = TypeHelper.BinaryNumericOperationType(left, right, unsigned);
						break;

					case BinaryOperation.And:
					case BinaryOperation.Or:
					case BinaryOperation.Xor:
						instruction.Result.Type = TypeHelper.BinaryLogicalOperationType(left, right);
						break;

					case BinaryOperation.Shl:
					case BinaryOperation.Shr:
						instruction.Result.Type = left;
						break;

					case BinaryOperation.Eq:
					case BinaryOperation.Neq:
						// If one of the operands has type Boolean,
						// then the other operand must also have type Boolean.
						if (left != null && left.Equals(PlatformTypes.Boolean))
						{
							instruction.RightOperand.Type = PlatformTypes.Boolean;
						}
						else if (right != null && right.Equals(PlatformTypes.Boolean))
						{
							instruction.LeftOperand.Type = PlatformTypes.Boolean;
						}

						instruction.Result.Type = PlatformTypes.Boolean;
						break;

					case BinaryOperation.Gt:
					case BinaryOperation.Ge:
					case BinaryOperation.Lt:
					case BinaryOperation.Le:
						// If one of the operands has a reference type,
						// then the operator must be != instead of >.
						if ((left != null && left.TypeKind == TypeKind.ReferenceType) ||
							(right != null && right.TypeKind == TypeKind.ReferenceType))
						{
							instruction.Operation = BinaryOperation.Neq;
						}

						instruction.Result.Type = PlatformTypes.Boolean;
						break;
				}
			}

			public override void Visit(ConditionalBranchInstruction instruction)
			{
				if (instruction.LeftOperand.Type != null &&
					instruction.RightOperand.Type == null)
				{
					instruction.RightOperand.Type = instruction.LeftOperand.Type;
				}
				else if (instruction.LeftOperand.Type == null &&
						 instruction.RightOperand.Type != null)
				{
					instruction.LeftOperand.Type = instruction.RightOperand.Type;
				}

				if (instruction.Operation == BranchOperation.Eq &&
					instruction.RightOperand is Constant &&
					instruction.LeftOperand.Type != null &&
					!instruction.LeftOperand.Type.Equals(PlatformTypes.Boolean))
				{
					var constant = instruction.RightOperand as Constant;

					if (constant.Value is bool)
					{
						var value = (bool)constant.Value;

						if (value)
						{
							// Change num == true to num != true
							instruction.Operation = BranchOperation.Neq;
						}

						if (instruction.LeftOperand.Type.TypeKind == TypeKind.ValueType)
						{
							// Change num == false to num == 0 or
							// num != true to num != 0
							constant.Value = 0;
						}
						else
						{
							// Change num == false to num == null or
							// num != true to num != null
							constant.Value = null;
						}

						constant.Type = instruction.LeftOperand.Type;
					}
				}
			}
		}

		#endregion

		private ControlFlowGraph cfg;
		private IType returnType;

		// Iteration bound + cycle detection close a DoS-shaped non-termination bug: untrusted IL
		// can drive the fixed point below to oscillate forever between distinct non-null types,
		// not just stall on a never-typed variable. See
		// docs/agent/measurements/typeinference-nontermination.md (typeinference-nontermination-spike,
		// typeinference-bound-sizing, cycle-detection-review) for the measurements behind these
		// numbers. DefaultMaxIterations keeps a >14x margin over the highest iteration count any
		// legitimately converging method in the measured corpus needed (max 7).
		public const int DefaultMaxIterations = 100;

		// Cap on retained cycle-detection history (see Analyze): every pathological cycle found
		// in the measured corpus resolved within 6 iterations, so this keeps a >3x margin while
		// bounding memory to O(window * variables) instead of O(maxIterations * variables) for a
		// method with many variables. A cycle longer than the window still terminates safely via
		// maxIterations.
		private const int CycleDetectionWindow = 20;

		public int IterationCount { get; private set; }
		public bool ReachedIterationLimit { get; private set; }

		public bool CycleDetected { get; private set; }

		/// <remarks>
		/// Callers must check this before trusting <see cref="Analyze"/>'s result: on
		/// non-convergence the variables carry the best-effort types found so far, not a
		/// validated fixed point, and nothing about that result on its own distinguishes it from
		/// a genuine convergence. Neither current caller in this repository
		/// (<c>Console/Program.cs</c>, <c>InterPointsToAnalysis.cs</c>) performs this check
		/// today -- a deliberate, separate decision, not an oversight of this flag.
		/// </remarks>
		public bool DidNotConverge => ReachedIterationLimit || CycleDetected;

		public TypeInferenceAnalysis(ControlFlowGraph cfg, IType returnType)
		{
			this.cfg = cfg;
			this.returnType = returnType;
		}

		/// <remarks>
		/// See docs/agent/measurements/typeinference-nontermination.md for the measurements
		/// behind <paramref name="maxIterations"/>'s default and <paramref name="detectCycles"/>'s
		/// cost/effectiveness. Cycle detection is sound against the state it compares (each
		/// variable's <see cref="IType"/>), but <see cref="TypeInferer"/>'s Visit methods also
		/// mutate state outside that snapshot -- <c>Constant.Value</c>/<c>Type</c>, and,
		/// permanently, <c>BinaryInstruction.Operation</c> for <c>Gt</c>/<c>Ge</c>/<c>Lt</c>/<c>Le</c>
		/// (rewritten to <c>Neq</c> the first time an operand is a reference type). An exact
		/// repeat of the compared variable-type state is therefore not a proof those other
		/// mutations have also stabilized, so this is a known, unaudited gap in the detector's
		/// soundness claim, not yet observed to misfire in the measured corpus. Either way, a
		/// caller gets a best-effort result on non-convergence with no signal distinguishing it
		/// from a converged one unless it checks <see cref="DidNotConverge"/> itself.
		/// </remarks>
		public void Analyze(int maxIterations = DefaultMaxIterations, bool detectCycles = true)
		{
			var inferer = new TypeInferer(returnType);
			var sorted_nodes = cfg.ForwardOrder;

			// Propagate types over the CFG until a fixedpoint is reached
			// (i.e. when types do not change anymore)
			bool changed;
			IterationCount = 0;
			ReachedIterationLimit = false;
			CycleDetected = false;

			// Hash-keyed, not a linear scan against every prior snapshot: comparing each new
			// state against every state seen so far is O(iterations^2 * variables), which a
			// method with many variables and no repeating state can drive to the full iteration
			// bound on both axes at once -- the same DoS shape this cycle detector exists to
			// close, one level down. A cheap order-independent hash buckets candidate states so
			// the common case is O(1) per iteration; StatesEqual only runs on a hash hit, so a
			// collision can cost an extra comparison but never cause a false cycle report.
			// History is capped to CycleDetectionWindow (see its own comment) to bound memory too.
			var seenStates = detectCycles ? new Dictionary<int, List<IDictionary<IVariable, IType>>>() : null;
			var seenOrder = detectCycles ? new Queue<(int Hash, IDictionary<IVariable, IType> State)>() : null;

			do
			{
				IterationCount++;
				var result = GetTypeInferenceResult();

				if (seenStates != null)
				{
					var hash = ComputeStateHash(result);

					if (seenStates.TryGetValue(hash, out var bucket))
					{
						if (bucket.Any(seen => StatesEqual(seen, result)))
						{
							CycleDetected = true;
							break;
						}
					}
					else
					{
						bucket = new List<IDictionary<IVariable, IType>>();
						seenStates[hash] = bucket;
					}

					bucket.Add(result);
					seenOrder.Enqueue((hash, result));

					if (seenOrder.Count > CycleDetectionWindow)
					{
						var evicted = seenOrder.Dequeue();
						var evictedBucket = seenStates[evicted.Hash];
						evictedBucket.Remove(evicted.State);
						if (evictedBucket.Count == 0) seenStates.Remove(evicted.Hash);
					}
				}

				for (var i = 0; i < sorted_nodes.Length; ++i)
				{
					var node = sorted_nodes[i];
					inferer.Visit(node);
				}

				changed = !SameTypes(result);

				if (changed && IterationCount >= maxIterations)
				{
					ReachedIterationLimit = true;
					break;
				}
			}
			while (changed);
		}

		private IDictionary<IVariable, IType> GetTypeInferenceResult()
		{
			var result = new Dictionary<IVariable, IType>();
			var variables = cfg.GetVariables();

			foreach (var variable in variables)
			{
				result[variable] = variable.Type;
			}

			return result;
		}

		private bool SameTypes(IDictionary<IVariable, IType> oldTypes)
		{
			var result = true;
			var variables = cfg.GetVariables();

			foreach (var variable in variables)
			{
				var oldType = oldTypes[variable];
				var newType = variable.Type;

				// Two nulls mean "still unknown", not "changed" -- only a null<->non-null
				// transition, or a genuine difference between two known types, counts as
				// changed. See docs/agent/measurements/typeinference-nontermination.md (4 of the
				// 21 methods that used to hang forever were stuck exactly this way).
				if ((oldType == null) != (newType == null) ||
					(oldType != null && !oldType.Equals(newType)))
				{
					result = false;
					break;
				}
			}

			return result;
		}

		// Cheap, order-independent hash over a type-inference state snapshot -- used only to
		// bucket candidates for StatesEqual in Analyze's cycle detector, never as a substitute
		// for it, so a hash collision can only cost an extra comparison, never a false cycle
		// report.
		private static int ComputeStateHash(IDictionary<IVariable, IType> state)
		{
			unchecked
			{
				var hash = 0;

				foreach (var entry in state)
				{
					var typeHash = entry.Value?.GetHashCode() ?? 0;
					hash ^= (entry.Key.GetHashCode() * 397) ^ typeHash;
				}

				return hash;
			}
		}

		// Exact structural equality between two state snapshots, same null/Equals predicate
		// SameTypes already uses per-variable. Reuses the two dictionaries' own keys instead of
		// calling cfg.GetVariables() again, since this now runs on every cycle-detection hash hit
		// rather than the whole retained history every iteration.
		private static bool StatesEqual(IDictionary<IVariable, IType> a, IDictionary<IVariable, IType> b)
		{
			if (a.Count != b.Count) return false;

			foreach (var entry in a)
			{
				if (!b.TryGetValue(entry.Key, out var otherType)) return false;

				var thisType = entry.Value;
				if ((thisType == null) != (otherType == null) ||
					(thisType != null && !thisType.Equals(otherType)))
				{
					return false;
				}
			}

			return true;
		}
	}
}
