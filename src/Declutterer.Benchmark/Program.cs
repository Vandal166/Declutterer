using BenchmarkDotNet.Running;
using Declutterer.Benchmark;

var summary = BenchmarkRunner.Run<DirectoryScanBenchmark>();


