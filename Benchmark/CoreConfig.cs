﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Validators;

namespace Benchmark
{
    public class CoreConfig : ManualConfig
    {
        public CoreConfig()
        {
            Add(JitOptimizationsValidator.FailOnError);
  
            Add(Job.Default
                .With(BenchmarkDotNet.Environments.Runtime.Core)
                .WithRemoveOutliers(false)
                .With(new GcMode() { Server = true })
                .With(RunStrategy.Throughput)
                .WithLaunchCount(3)
                .WithWarmupCount(5)
                .WithTargetCount(10));
        }
    }
}