using System;
using System.Collections.Generic;
using System.Linq;
using CG.Framework.Engines;
using CG.Framework.Engines.Models;
using CG.Framework.Helper;

namespace CG.Language.Helper;

// PluginUtils.GetPackageByTypeName(packages, pCppName)
internal readonly struct PackageSorterResult<T>
{
    public List<T> SortedList { get; init; }
    public Dictionary<T, T> CycleList { get; init; }

    public List<T> GetFulList()
    {
        var ret = new List<T>(SortedList);
        ret.AddRange(CycleList.Keys);
        return ret;
    }
}

// Thanks To https://stackoverflow.com/questions/4106862/how-to-sort-depended-objects-by-dependency
// Thanks To https://www.codeproject.com/Articles/869059/Topological-sorting-in-Csharp
internal static class PackageSorter
{
    private static bool PackageDependencyComparer(IEnginePackage lhs, IEnginePackage rhs)
    {
        if (!rhs.Dependencies.IsEmpty())
            return false;

        if (rhs.Dependencies.Contains(lhs.CppName))
            return true;

        if (lhs.Dependencies.Contains(rhs.CppName))
            return false;

        /*
        foreach (UEObject dep in rhs.Dependencies.Cast<UEObject>())
        {
            if (!PackageMap.ContainsKey(dep))
                continue; // Missing package, should not occur...

            Package package = PackageMap[dep];
            if (PackageDependencyComparer(lhs, package))
                return true;
        }
        */

        return false;
    }

    private static bool Visit<T>(T item, Func<T, IEnumerable<T>> getDependencies, ICollection<T> sorted, IDictionary<T, T> cycleList, IDictionary<T, bool> visited)
    {
        bool alreadyVisited = visited.TryGetValue(item, out bool inProcess);

        if (alreadyVisited)
        {
            if (inProcess)
            {
                return false;
            }
        }
        else
        {
            visited[item] = true;

            List<T> dependencies = getDependencies(item).ToList();
            foreach (T dependency in dependencies)
            {
                // If dependency already known as have dependency cycle
                // then just pass it to keep sort of that package work
                if (cycleList.ContainsKey(dependency))
                    continue;

                bool dependencyCycle = !Visit(dependency, getDependencies, sorted, cycleList, visited);
                if (!dependencyCycle)
                    continue;

                cycleList.Add(item, dependency);
                //cycleList.Add(dependency);
                break;
            }

            visited[item] = false;

            if (!cycleList.ContainsKey(item))
                sorted.Add(item);
        }

        return true;
    }

    private static PackageSorterResult<T> Sort<T>(this IEnumerable<T> source, Func<T, IEnumerable<T>> getDependencies)
    {
        var sorted = new List<T>();
        var cycleList = new Dictionary<T, T>();
        var visited = new Dictionary<T, bool>();

        foreach (T item in source)
            Visit(item, getDependencies, sorted, cycleList, visited);

        return new PackageSorterResult<T>() { SortedList = sorted, CycleList = cycleList };
    }

    public static PackageSorterResult<IEnginePackage> Sort(List<IEnginePackage> packages)
    {
        return packages.Sort(pack =>
        {
            return pack.Dependencies
                .Select(pCppName => PluginUtils.GetPackageByTypeName(packages, pCppName))
                .Where(p => p is not null && p.CppName != pack.CppName)
                .GroupBy(p => p.CppName)
                .Select(g => g.First())
                .ToList();
        });

        /*
        // Packages sorter
        try
        {
        }
        catch (Exception e)
        {
            for (int i = 0; i < packages.Count - 1; i++)
            {
                for (int j = 0; j < packages.Count - i - 1; j++)
                {
                    if (!PackageDependencyComparer(packages[j], packages[j + 1]))
                        packages.Reverse(j, 2); // Will swap elements with index (j, j + 1).
                }
            }
            CheatGearInterface.Default.ExceptionThrower(e);
        }

        return packages;
        */
    }

    public static void SortStructsClassesInPackages(IReadOnlyList<IEnginePackage> packages)
    {
        // Sort Classes/Structs in the package itself
        foreach (IEnginePackage pack in packages.Where(p => !p.IsPredefined))
        {
            // Init class dependencies
            foreach (EngineClass c in pack.Classes)
            {
                if (c.Supers.Count == 0)
                    continue;

                foreach ((string sName, string sCppName) in c.Supers)
                {
                    EngineStruct super = PluginUtils.GetClassFromPackages(packages, sCppName)
                                         ?? PluginUtils.GetStructFromPackages(packages, sCppName);
                    if (super is not null)
                        c.AddDependency(super);
                }
            }

            // Init struct dependencies
            foreach (EngineStruct ss in pack.Structs)
            {
                foreach ((string sName, string sCppName) in ss.Supers)
                {
                    EngineStruct super = PluginUtils.GetStructFromPackages(packages, sCppName)
                                         ?? PluginUtils.GetClassFromPackages(packages, sCppName);
                    if (super is not null)
                        ss.AddDependency(super);
                }

                // Variables
                IEnumerable<string> memDependencies = PluginUtils.GetDependencyCppNameFromMembers(ss.Variables);
                foreach (string dependency in memDependencies)
                {
                    EngineStruct debStruct = PluginUtils.GetStructFromPackages(packages, dependency)
                                             ?? PluginUtils.GetClassFromPackages(packages, dependency);
                    if (debStruct is not null)
                        ss.AddDependency(debStruct);
                }
            }

            // Class
            PackageSorterResult<EngineClass> classesSorted = pack.Classes
                .GroupBy(c => c.NameCpp)
                .Select(g => g.First())
                .Sort(c =>
                {
                    return c.Dependencies.Where(ss => pack.Classes.Contains(ss))
                        .Cast<EngineClass>() // Get only classes in this package
                        .ToList();
                });
            List<EngineClass> classList = classesSorted.GetFulList();
            classList.Reverse();
            pack.Classes.Clear();
            pack.Classes.AddRange(classList);

            // Structs
            PackageSorterResult<EngineStruct> structsSorted = pack.Structs
                .GroupBy(ss => ss.NameCpp)
                .Select(g => g.First())
                .Sort(ss =>
                {
                    return ss.Dependencies.Where(@struct =>
                        pack.Structs.Contains(@struct)); // Get only structs in this package
                });
            List<EngineStruct> structList = structsSorted.GetFulList();
            structList.Reverse();
            pack.Structs.Clear();
            pack.Structs.AddRange(structList);
        }
    }
}
