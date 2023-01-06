﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using Xunit;
using static Microsoft.PowerFx.Core.Localization.TexlStrings;

namespace Microsoft.PowerFx.Tests
{
    // These tests illustrate various sandboxing features, such as protection against:
    // - long-running scripts,
    // - CPU usage,
    // - memory usage,
    // - stack overflows, etc.
    public class SandboxTests
    {
        // Protect against stack overflows. 
        [Fact]
        public async void MaxRecursionDepthTest()
        {
            var config = new PowerFxConfig(null)
            {
                MaxCallDepth = 5
            };
            var recalcEngine = new RecalcEngine(config);
            Assert.IsType<ErrorValue>(recalcEngine.Eval("Abs(Abs(Abs(Abs(Abs(Abs(1))))))"));
            Assert.IsType<NumberValue>(recalcEngine.Eval("Abs(Abs(Abs(Abs(Abs(1)))))"));
            Assert.IsType<NumberValue>(recalcEngine.Eval(
                @"Sum(
                Sum(Sum(1),1),
                Sum(Sum(1),1),
                Sum(Sum(1),1)
                )"));
        }

        // Create a small expression that runs quickly and rapidly consumes large amounts of memory. 
        // Memory used here is **expontential** :  O[nWidth ^ nDepth] 
        private static string CreateMemoryExpression(int nWidth, int nDepth)
        {
            // Substitute() function can quickly expand to consume a lot of memory 
            // Substitute("aaa", "a", "12345678910") // result is Len(arg0)*Len(arg2) = 30 chars

            var a = "\"a\"";
            var aa = "\"" + new string('a', nWidth) + "\"";
            var left = $"Substitute(";
            var right = $", {a}, {aa})";
            var sb = new StringBuilder();
            for (var i = 0; i < nDepth; i++)
            {
                sb.Append(left);
            }

            sb.Append(a);
            for (var i = 0; i < nDepth; i++)
            {
                sb.Append(right);
            }

            var expr = sb.ToString();
            return expr;
        }

        // Pick a "small" memory size that's large enough to execute basic expressions but will
        // fail on intentionally large expressions. 
        private const int DefaultMemorySizeBytes = 50 * 1000;

        // Verify memory limits. 
        [Theory]
        [InlineData(10, 15)]
        [InlineData(100, 4)]
        [InlineData(50, 20)]
        public async Task MemoryLimit(int nWidth, int nDepth)
        {
            var engine = new RecalcEngine();

            var mem = new BasicGovernor(DefaultMemorySizeBytes);

            var runtimeConfig = new RuntimeConfig();
            runtimeConfig.AddService<Governor>(mem);

            var expr = CreateMemoryExpression(nWidth, nDepth);

            // Ensure governor traps excessive memory usage. 
            await Assert.ThrowsAsync<GovernorException>(async () =>
                    await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig));

#if false // https://github.com/microsoft/Power-Fx/issues/971
            // Still traps without the pre-poll. 
            // The pre-poll may fail the expression before it even executes. 
            // So skipping pre-poll tests the execution can be aborted. 
            var gov2 = new TestIgnorePrePollGovernor(DefaultMemorySizeBytes);
            runtimeConfig.AddService<Governor>(gov2);

            await Assert.ThrowsAsync<MyException>(async () =>
                    await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig));
#endif
        }

        // We can recover after a oom expression
        [Fact]
        public async Task MemoryLimitRecover()
        {
            var engine = new RecalcEngine();

            var mem = new BasicGovernor(DefaultMemorySizeBytes);

            var runtimeConfig = new RuntimeConfig();
            runtimeConfig.AddService<Governor>(mem);

            var expr = CreateMemoryExpression(10, 15); // will cause OOM
            var smallExpr = CreateMemoryExpression(1, 1); // should b eok

            // Governor allows basic expressions
            var result = await engine.EvalAsync(smallExpr, CancellationToken.None, runtimeConfig: runtimeConfig);
            mem.Poll();

            // Ensure governor traps excessive memory usage. 
            await Assert.ThrowsAsync<GovernorException>(async () =>
                    await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig));

            // Since Governor is cumulative, even the small evaluations now fails
            Assert.Throws<GovernorException>(() => mem.Poll());

            // But creating a new one works. 
            runtimeConfig.AddService<Governor>(new BasicGovernor(DefaultMemorySizeBytes));
            result = await engine.EvalAsync(smallExpr, CancellationToken.None, runtimeConfig: runtimeConfig);
        }

        private class TestIgnorePrePollGovernor : BasicGovernor
        {
            public TestIgnorePrePollGovernor(long maxAllowedBytes)
                : base(maxAllowedBytes)
            {
            }

            public override void PollMemory(long allocateBytes)
            {
                // nop. Ignore these warnings. Just rely on Poll.
            }
        }

        // Verify cancellation.
        // Expression that takes a long time to run.  Doesn't need much stack or memory.  
        [Theory]
        [InlineData("ForAll(Sequence(K), ForAll(Sequence(K), ForAll(Sequence(K), 5)))")]
        [InlineData("ForAll(Sequence(1000), Max(Sequence(K), Value))")]
        public async Task InfiniteLoop(string expr)
        {
            var engine = new RecalcEngine();
            engine.UpdateVariable("K", 5);

            // Can be invoked. 
            using var cts = new CancellationTokenSource();

            cts.CancelAfter(5);

            // Eval may never return.             
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await engine.EvalAsync(expr, cts.Token);
            });
        }
    }
}