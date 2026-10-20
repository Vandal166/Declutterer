using System;
using System.Collections.Generic;
using System.IO;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup.Rules;

/// <summary>
/// Build output and dependency folders that tooling can recreate (node_modules, bin/obj, target, __pycache__ ...).
/// Generic names such as "bin" or "build" only count when the neighbouring files prove which kind of project this is.
/// </summary>
public sealed class RegenerableArtifactRule : IAssessmentRule
{
    private sealed record Art(ProjectMarkers ParentMarkers, double WithMarker, double WithoutMarker, double Risk, string Label,
        ProjectMarkers OwnMarkers = ProjectMarkers.None);

    private const ProjectMarkers NetLike = ProjectMarkers.DotNetProject | ProjectMarkers.Solution;

    private static readonly Dictionary<string, Art> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["node_modules"] = new(ProjectMarkers.NodePackage, 4.6, 3.2, 0.03, "npm dependencies"),
        ["__pycache__"] = new(ProjectMarkers.None, 4.6, 4.6, 0.01, "Python bytecode cache"),
        [".pytest_cache"] = new(ProjectMarkers.None, 4.2, 4.2, 0.02, "pytest cache"),
        [".mypy_cache"] = new(ProjectMarkers.None, 4.2, 4.2, 0.02, "mypy cache"),
        [".ruff_cache"] = new(ProjectMarkers.None, 4.2, 4.2, 0.02, "ruff cache"),
        [".tox"] = new(ProjectMarkers.None, 3.8, 3.8, 0.04, "tox environments"),
        [".nox"] = new(ProjectMarkers.None, 3.8, 3.8, 0.04, "nox environments"),
        [".ipynb_checkpoints"] = new(ProjectMarkers.None, 3.5, 3.5, 0.05, "notebook checkpoints"),
        ["obj"] = new(NetLike, 4.2, 0, 0.03, ".NET intermediate output"),
        ["bin"] = new(NetLike, 3.8, 0, 0.05, ".NET build output"),
        [".vs"] = new(NetLike, 4.0, 2.4, 0.04, "Visual Studio cache"),
        ["target"] = new(ProjectMarkers.Cargo | ProjectMarkers.Maven, 4.0, 0, 0.04, "Rust/Maven build output"),
        ["build"] = new(ProjectMarkers.Gradle | ProjectMarkers.CMake | ProjectMarkers.Maven, 3.0, 0, 0.06, "build output"),
        ["dist"] = new(ProjectMarkers.NodePackage, 2.2, 0, 0.08, "distribution output"),
        ["out"] = new(ProjectMarkers.NodePackage | ProjectMarkers.Gradle, 2.2, 0, 0.08, "build output"),
        [".gradle"] = new(ProjectMarkers.Gradle, 4.0, 3.6, 0.04, "Gradle cache"),
        [".next"] = new(ProjectMarkers.NodePackage, 3.8, 2.4, 0.04, "Next.js build cache"),
        [".nuxt"] = new(ProjectMarkers.NodePackage, 3.8, 2.4, 0.04, "Nuxt build cache"),
        [".svelte-kit"] = new(ProjectMarkers.NodePackage, 3.8, 2.4, 0.04, "SvelteKit build cache"),
        [".angular"] = new(ProjectMarkers.NodePackage, 3.8, 2.4, 0.04, "Angular cache"),
        [".parcel-cache"] = new(ProjectMarkers.NodePackage, 3.8, 3.0, 0.03, "Parcel cache"),
        [".turbo"] = new(ProjectMarkers.NodePackage, 3.8, 3.0, 0.03, "Turborepo cache"),
        ["coverage"] = new(ProjectMarkers.NodePackage, 2.5, 0, 0.08, "coverage report"),
        [".terraform"] = new(ProjectMarkers.None, 3.0, 3.0, 0.06, "Terraform provider cache"),
        ["pods"] = new(ProjectMarkers.Podfile, 3.0, 0, 0.06, "CocoaPods dependencies"),
        ["deriveddata"] = new(ProjectMarkers.None, 3.5, 3.5, 0.04, "Xcode derived data"),
        [".venv"] = new(ProjectMarkers.None, 3.2, 0, 0.12, "Python virtual environment", ProjectMarkers.PythonVenv),
        ["venv"] = new(ProjectMarkers.None, 3.2, 0, 0.12, "Python virtual environment", ProjectMarkers.PythonVenv),
        ["env"] = new(ProjectMarkers.None, 3.0, 0, 0.12, "Python virtual environment", ProjectMarkers.PythonVenv),
    };

    // Everything below these is regenerable by definition.
    private static readonly string[] RegenerableAncestors = { "node_modules", "__pycache__" };

    public void Evaluate(AssessmentContext ctx, SignalSink sink)
    {
        // Inside a regenerable tree (e.g. node_modules\left-pad\index.js).
        var segs = ctx.Segments;
        for (int i = 0; i < segs.Length - 1; i++)
        {
            foreach (var anc in RegenerableAncestors)
            {
                if (string.Equals(segs[i], anc, StringComparison.OrdinalIgnoreCase))
                {
                    sink.Add(ReasonCode.RegenerableBuildOutput, EvidenceGroup.Location, 3.5, 0.03, $"inside '{anc}'");
                    return;
                }
            }
        }

        if (!ctx.IsDirectory || !Table.TryGetValue(ctx.Name, out var art))
            return;

        double llr = 0;
        string why = string.Empty;

        if (art.OwnMarkers != ProjectMarkers.None)
        {
            // virtual environments are recognised by their own pyvenv.cfg
            if ((ctx.Markers.GetOwn(ctx.Path) & art.OwnMarkers) != 0)
            {
                llr = art.WithMarker;
                why = "contains pyvenv.cfg";
            }
        }
        else
        {
            var parent = Path.GetDirectoryName(PathKey.Normalize(ctx.Path));
            var parentMarkers = string.IsNullOrEmpty(parent) ? ProjectMarkers.None : ctx.Markers.GetOwn(parent);

            if (art.ParentMarkers == ProjectMarkers.None)
            {
                llr = art.WithMarker;
                why = "name is specific to generated output";
            }
            else if ((parentMarkers & art.ParentMarkers) != 0)
            {
                llr = art.WithMarker;
                why = "next to " + DescribeMarker(parentMarkers & art.ParentMarkers);
            }
            else
            {
                llr = art.WithoutMarker;
                why = "no project file next to it";
            }
        }

        if (llr > 0)
            sink.Add(ReasonCode.RegenerableBuildOutput, EvidenceGroup.Location, llr, art.Risk, $"{art.Label} ({why})");
    }

    private static string DescribeMarker(ProjectMarkers m) =>
        m.HasFlag(ProjectMarkers.NodePackage) ? "package.json" :
        m.HasFlag(ProjectMarkers.Solution) ? "a solution file" :
        m.HasFlag(ProjectMarkers.DotNetProject) ? "a project file" :
        m.HasFlag(ProjectMarkers.Cargo) ? "Cargo.toml" :
        m.HasFlag(ProjectMarkers.Maven) ? "pom.xml" :
        m.HasFlag(ProjectMarkers.Gradle) ? "a Gradle build file" :
        m.HasFlag(ProjectMarkers.CMake) ? "CMakeLists.txt" :
        m.HasFlag(ProjectMarkers.Podfile) ? "a Podfile" : "a project file";
}
