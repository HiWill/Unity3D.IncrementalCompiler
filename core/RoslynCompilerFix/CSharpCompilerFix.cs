﻿using System;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace RoslynCompilerFix
{
    internal class CSharpCompilerFix
    {
        public static void Process(AssemblyDefinition assemblyDef)
        {
            foreach (var module in assemblyDef.Modules)
            {
                var generatedNames = module.GetType("Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNames");
                if (generatedNames != null)
                {
                    foreach (var method in generatedNames.Methods)
                    {
                        if (method.Name == "MakeHoistedLocalFieldName")
                            Fix_GeneratedNames_MakeHoistedLocalFieldName(method);
                        else if (method.Name == "MakeMethodScopedSynthesizedName")
                            Fix_GeneratedNames_MakeMethodScopedSynthesizedName(method);
                        else if (method.Name == "ThisProxyFieldName")
                            Fix_GeneratedNames_ThisProxyFieldName(method);
                        else if (method.Name == "MakeLambdaMethodName")
                            Fix_GeneratedNames_MakeLambdaMethodName(method);
                    }
                }

                var lambdaFrame = module.GetType("Microsoft.CodeAnalysis.CSharp.LambdaFrame");
                if (lambdaFrame != null)
                {
                    foreach (var method in lambdaFrame.Methods)
                    {
                        if (method.Name == ".ctor")
                            Fix_LambdaFrame_Constructor(method);
                    }
                }
            }
        }

        private static void Fix_GeneratedNames_MakeHoistedLocalFieldName(MethodDefinition method)
        {
            // Goal:
            //   Rename <v>4__1 to <v>__1 for local variables in iterator
            //
            // How for Roslyn:
            //   Remove following code from GeneratedNames.MakeHoistedLocalFieldName
            //     builder.Append((char)GeneratedNameKind.HoistedLocalField);
            //
            // How for IL:
            //   Remove following code from body of method
            //     IL_003c: ldloc.0
            //     IL_003d: ldc.i4.s 53
            //     IL_003f: callvirt System.Text.StringBuilder System.Text.StringBuilder::Append(System.Char)
            //     IL_0044: pop

            var il = method.Body.GetILProcessor();
            for (var i = 0; i < il.Body.Instructions.Count; i++)
            {
                var inst = il.Body.Instructions[i];
                if (inst.OpCode.Code == Code.Ldc_I4_S && (sbyte)inst.Operand == 53)
                {
                    for (int j = 0; j < 4; j++)
                        il.Remove(il.Body.Instructions[i + 2 - j]);
                    break;
                }
            }
        }

        private static void Fix_GeneratedNames_MakeMethodScopedSynthesizedName(MethodDefinition method)
        {
            // Goal:
            //   Rename <TestCoroutine>d__1 to <TestCoroutine>c__Iterator0 for iterator class name
            //
            // How for Roslyn:
            //   Append code following code at GeneratedNames.MakeMethodScopedSynthesizedName
            //     builder.Append('>');
            //     builder.Append((char)kind);
            //     >> if (kind == GeneratedNameKind.StateMachineType) {
            //     >>   builder.Append("__Iterator")
            //     >> }
            //
            // How for IL:
            //   IL_0033: ldloc.0
            //   IL_0034: ldc.i4.s 62
            //   IL_0036: callvirt instance class [System.Runtime]System.Text.StringBuilder [System.Runtime]System.Text.StringBuilder::Append(char)
            //   IL_003b: pop
            //   IL_003c: ldloc.0
            //   IL_003d: ldarg.0
            //   IL_003e: conv.u2
            //   IL_003f: callvirt instance class [System.Runtime]System.Text.StringBuilder [System.Runtime]System.Text.StringBuilder::Append(char)
            //   IL_0044: pop
            //   >> IL_?: ldarg.0
            //   >> IL_?: ldc.i4.s IL_BASE
            //   >> IL_?: bne.un.s OUT
            //   >> IL_?: ldloc.0
            //   >> IL_?: ldstr "__Iterator"
            //   >> IL_?: callvirt instance class [mscorlib]System.Text.StringBuilder [mscorlib]System.Text.StringBuilder::Append(string)
            //   >> IL_?: pop
            //   IL_BASE:

            var il = method.Body.GetILProcessor();
            for (var i = 0; i < il.Body.Instructions.Count; i++)
            {
                var inst = il.Body.Instructions[i];
                if (inst.OpCode.Code == Code.Ldc_I4_S && (sbyte)inst.Operand == 62)
                {
                    var baseInst = il.Body.Instructions[i + 8];

                    var appendStringMethod = typeof(StringBuilder).GetMethod("Append", new Type[] { typeof(string) });
                    var appendString = method.Module.Import(appendStringMethod);

                    il.InsertBefore(baseInst, il.Create(OpCodes.Ldarg_0));
                    il.InsertBefore(baseInst, il.Create(OpCodes.Ldc_I4_S, (sbyte)100));
                    il.InsertBefore(baseInst, il.Create(OpCodes.Bne_Un_S, baseInst));
                    il.InsertBefore(baseInst, il.Create(OpCodes.Ldloc_0));
                    il.InsertBefore(baseInst, il.Create(OpCodes.Ldstr, "__Iterator"));
                    il.InsertBefore(baseInst, il.Create(OpCodes.Callvirt, appendString));
                    il.InsertBefore(baseInst, il.Create(OpCodes.Pop));
                    break;
                }
            }
        }

        private static void Fix_GeneratedNames_ThisProxyFieldName(MethodDefinition method)
        {
            // Goal:
            //   Rename <>4__this to <>f__this for this in iterator class
            //
            // How for Roslyn:
            //   Modify return statement of GeneratedNames.ThisProxyFieldNam from
            //     return "<>4__this";
            //   to
            //     return "<>f__this";
            //
            // How for IL:
            //   Change literal "<>4__this" to "<>f__this" at ThisProxyFieldName()
            //     IL_0000: ldstr "<>4__this"
            //     IL_0005: ret

            var il = method.Body.GetILProcessor();
            for (var i = 0; i < il.Body.Instructions.Count; i++)
            {
                var inst = il.Body.Instructions[i];
                if (inst.OpCode.Code == Code.Ldstr && (string)inst.Operand == "<>4__this")
                {
                    il.Replace(inst, il.Create(OpCodes.Ldstr, "<>f__this"));
                    break;
                }
            }
        }

        private static void Fix_LambdaFrame_Constructor(MethodDefinition method)
        {
            // Goal:
            //   Rename <>c__DisplayClass0_0 to <*MethodName*>c__AnonStorey* for lambda class name
            //
            // How for Roslyn:
            //   Modify constructor of LambdaFrame from
            //      LambdaFrame(MethodSymbol topLevelMethod, ...)
            //       : base(MakeName(...
            //   to
            //      LambdaFrame(MethodSymbol topLevelMethod, ...)
            //       : base("<" + topLevelMethod.Name + ">c__AnonStorey" + MakeName(...
            //
            // How for IL:
            //   IL_0000: ldarg.0
            //   >> IL_?: ldstr "<"
            //   >> IL_?: ldarg.1
            //   >> IL_?: callvirt instance string Microsoft.CodeAnalysis.CSharp.Symbol::get_Name()
            //   >> IL_?: ldstr ">c__AnonStorey"
            //   IL_0001: ldarg.2
            //   IL_0002: ldarg.3
            //   IL_0003: ldarg.s closureId
            //   IL_0005: call string Microsoft.CodeAnalysis.CSharp.LambdaFrame::MakeName(class Microsoft.CodeAnalysis.SyntaxNode, valuetype Microsoft.CodeAnalysis.CodeGen.DebugId, valuetype Microsoft.CodeAnalysis.CodeGen.DebugId)
            //   >> IL_?: call string [mscorlib]System.String::Concat(string, string, string, string)
            //   IL_000a: ldarg.1
            //   IL_000b: call instance void Microsoft.CodeAnalysis.CSharp.Symbols.SynthesizedContainer::.ctor(string, class Microsoft.CodeAnalysis.CSharp.Symbols.MethodSymbol)

            var il = method.Body.GetILProcessor();
            var baseInst = il.Body.Instructions[1];
            var baseInst2 = il.Body.Instructions[4];
            if (baseInst.OpCode.Code != Code.Ldarg_2)
                throw new InvalidOperationException();
            if (baseInst2.OpCode.Code != Code.Call)
                throw new InvalidOperationException();

            var symbolType = method.Module.GetType("Microsoft.CodeAnalysis.CSharp.Symbol");
            var symbolGetNameMethod = symbolType.Methods.FirstOrDefault(m => m.Name == "get_Name");
            var symbolGetName = method.Module.Import(symbolGetNameMethod);

            il.InsertBefore(baseInst, il.Create(OpCodes.Ldstr, "<"));
            il.InsertBefore(baseInst, il.Create(OpCodes.Ldarg_1));
            il.InsertBefore(baseInst, il.Create(OpCodes.Callvirt, symbolGetName));
            il.InsertBefore(baseInst, il.Create(OpCodes.Ldstr, ">c__AnonStorey"));

            var concat4Method = typeof(string).GetMethod("Concat", new Type[] { typeof(string), typeof(string), typeof(string), typeof(string) });
            var concat4 = method.Module.Import(concat4Method);

            il.InsertAfter(baseInst2, il.Create(OpCodes.Call, concat4));
        }

        private static void Fix_GeneratedNames_MakeLambdaMethodName(MethodDefinition method)
        {
            // Goal:
            //   Rename <Start>b__0 to <>m__<Start>b__0 for method name in lambda class
            //
            // How for Roslyn:
            //   Modify return statement of GeneratedNames.MakeLambdaMethodName from
            //     return MakeMethodScopedSynthesizedName(GeneratedNameKind.LambdaMethod, ...
            //   to
            //     return "<>m__" + MakeMethodScopedSynthesizedName(GeneratedNameKind.LambdaMethod, ...
            //
            // How for IL:
            //   >> IL_?: ldstr "<>m__"
            //   IL_0000: ldc.i4.s 57
            //   IL_0002: ldarg.0
            //   IL_0003: ldarg.1
            //   IL_0004: ldnull
            //   IL_0005: ldnull
            //   IL_0006: ldarg.2
            //   IL_0007: ldarg.3
            //   IL_0008: ldc.i4.0
            //   IL_0009: call string Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNames::MakeMethodScopedSynthesizedName(valuetype Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNameKind, int32, int32, string, string, int32, int32, bool)
            //   >> IL_?: call string [mscorlib]System.String::Concat(string, string)
            //   IL_000e: ret

            var il = method.Body.GetILProcessor();
            var baseInst = il.Body.Instructions[0];
            var baseInst2 = il.Body.Instructions[il.Body.Instructions.Count - 1];

            var concatMethod = typeof(string).GetMethod("Concat", new Type[] { typeof(string), typeof(string) });
            var concat = method.Module.Import(concatMethod);

            il.InsertBefore(baseInst, il.Create(OpCodes.Ldstr, "<>m__"));
            il.InsertBefore(baseInst2, il.Create(OpCodes.Call, concat));
        }
    }
}
