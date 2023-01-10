using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography.X509Certificates;
using CG.Framework.Engines;
using CG.Framework.Engines.Models;
using CG.Framework.Engines.Unreal;
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
        ret.AddRange(CycleList.Keys.Where(x => !ret.Contains(x)));
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


    // PluginUtils is missing this
    public static EngineEnum? GetEnumFromPackages(
        IEnumerable<IEnginePackage> packages,
        string cppName)
    {
        return packages.Where(p => p.Enums.Any(ss => ss.NameCpp == cppName)).Select(p => p.Enums.FirstOrDefault(ss => ss.NameCpp == cppName)).FirstOrDefault();
    }

    private static List<string> AlreadyMoved = new();
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

                bool dependencyCycle;
                //if (cycleList.ContainsKey(dependency))
                //    dependencyCycle = true;
                //else
                    dependencyCycle = !Visit(dependency, getDependencies, sorted, cycleList, visited);
                if (!dependencyCycle)
                    continue;

                cycleList.Add(item, dependency);
                break;

                // basically this entire block of code just moves cyclic dependencies to the topmost class or smth, idk it's just magic really.

                // TODO: add method arguments and return types maybe...
                // add super and field dependencies that aren't added normally.
                // e.g. class UActiveStorySpawnRequirement : public USpawnRequirement in SOT
                //var depList = new List<IEnginePackage> { (IEnginePackage)dependency };
                //// var depCS = ((IEnginePackage)dependency).Classes.Concat(((IEnginePackage)dependency).Structs).Select(x => x.NameCpp).Concat(((IEnginePackage)dependency).Fields.Select(x => x.Type));
                //var depCS1 = ((IEnginePackage)dependency).Classes.Concat(((IEnginePackage)dependency).Structs);
                //var depCS2 = depCS1.Select(x => x.Fields).Select(x => x.Select(f => f.Type));
                //var depCS = depCS1.Select(x => x.NameCpp);
                //foreach (var ss in depCS2)
                //foreach (var s in ss)
                //    if (!depCS.Contains(s))
                //        depCS.Append(s);

                //var itemList = new List<IEnginePackage> { (IEnginePackage)item };
                //var itemCS1 = ((IEnginePackage)item).Classes.Concat(((IEnginePackage)item).Structs);
                //var itemCS2 = itemCS1.Select(x => x.Fields).Select(x => x.Select(f => f.Type));
                //var itemCS = itemCS1.Select(x => x.NameCpp);
                //foreach (var ss in itemCS2)
                //    foreach (var s in ss)
                //        if (!itemCS.Contains(s))
                //            itemCS.Append(s);

                //foreach (var d in itemCS)
                //{
                //    var c = PluginUtils.GetClassFromPackages(itemList, d) ?? PluginUtils.GetStructFromPackages(itemList, d);
                //    if (c == null)
                //        continue;

                //    var sups = c.Supers.Where(x =>
                //        depCS.Contains((PluginUtils.GetClassFromPackages(depList, x.Value) ??
                //                        PluginUtils.GetStructFromPackages(depList, x.Value))?.NameCpp) && !((IEnginePackage)item).Dependencies.Contains(x.Value)).Select(x => x.Value);

                //    ((IEnginePackage)item).Dependencies.AddRange(sups);
                //}

                //foreach (var d in depCS)
                //{
                //    var c = PluginUtils.GetClassFromPackages(depList, d) ?? PluginUtils.GetStructFromPackages(depList, d);
                //    if (c == null)
                //        continue;

                //    var sups = c.Supers.Where(x =>
                //        itemCS.Contains((PluginUtils.GetClassFromPackages(itemList, x.Value) ??
                //                        PluginUtils.GetStructFromPackages(itemList, x.Value))?.NameCpp) && !((IEnginePackage)dependency).Dependencies.Contains(x.Value)).Select(x => x.Value);

                //    ((IEnginePackage)dependency).Dependencies.AddRange(sups);
                //}

                // classes, structs dependencies
                //var itemCSD = ((IEnginePackage)item).Classes.Concat(((IEnginePackage)item).Structs).SelectMany(x => x.Dependencies.Select(d => d.NameCpp));
                //var depCSD = ((IEnginePackage)dependency).Classes.Concat(((IEnginePackage)dependency).Structs).SelectMany(x => x.Dependencies.Select(d => d.NameCpp));

                //Console.Write(((IEnginePackage)item).Name + " deps: ");
                //((IEnginePackage)item).Dependencies.ForEach(x => Console.Write(x + ", "));
                //Console.WriteLine();

                //Console.Write(((IEnginePackage)dependency).Name + " deps: ");
                //((IEnginePackage)dependency).Dependencies.ForEach(x => Console.Write(x + ", "));
                //Console.WriteLine();

                // get dependencies of item that exist in dependency
                var itemClassDeps = new List<EngineClass>();
                var itemStructDeps = new List<EngineStruct>();
                var itemEnumDeps = new List<EngineEnum>();
                foreach (var d in ((IEnginePackage)item).Structs.SelectMany(x => x.Dependencies.Select(d => d.NameCpp)).Concat(((IEnginePackage)item).Dependencies).Distinct())
                {
                    itemClassDeps.AddRange(((IEnginePackage)dependency).Classes.Where(x => x.NameCpp == d)); // dependencies of each class
                    // itemClassDeps.AddRange(((IEnginePackage)dependency).Classes.Where(x => x.Dependencies.Any(de => de.NameCpp == d))); // dependencies of each dependency class and struct (fields, methods)
                    itemStructDeps.AddRange(((IEnginePackage)dependency).Structs.Where(x => x.NameCpp == d));
                    // itemStructDeps.AddRange(((IEnginePackage)dependency).Structs.Where(x => x.Dependencies.Any(de => de.NameCpp == d)));
                    itemEnumDeps.AddRange(((IEnginePackage)dependency).Enums.Where(x => x.NameCpp == d));
                }

                // get dependencies of dependency that exist in item
                var depClassDeps = new List<EngineClass>();
                var depStructDeps = new List<EngineStruct>();
                var depEnumDeps = new List<EngineEnum>();
                // foreach (var d in ((IEnginePackage)dependency).Dependencies)
                foreach (var d in ((IEnginePackage)dependency).Structs.SelectMany(x => x.Dependencies.Select(d => d.NameCpp)).Concat(((IEnginePackage)dependency).Dependencies).Distinct())
                {
                    depClassDeps.AddRange(((IEnginePackage)item).Classes.Where(x => x.NameCpp == d));
                    // depClassDeps.AddRange(((IEnginePackage)item).Classes.Where(x => x.Dependencies.Any(de => de.NameCpp == d)));
                    depStructDeps.AddRange(((IEnginePackage)item).Structs.Where(x => x.NameCpp == d));
                    // depStructDeps.AddRange(((IEnginePackage)item).Structs.Where(x => x.Dependencies.Any(de => de.NameCpp == d)));
                    depEnumDeps.AddRange(((IEnginePackage)item).Enums.Where(x => x.NameCpp == d));
                }

                var itemCyclic = ((IEnginePackage)item).Dependencies.Where(x =>
                    itemClassDeps.Any(d => d.NameCpp == x) ||
                    itemStructDeps.Any(d => d.NameCpp == x) ||
                    itemEnumDeps.Any(d => d.NameCpp == x));

                var dependencyCyclic = ((IEnginePackage)dependency).Dependencies.Where(x =>
                    depClassDeps.Any(d => d.NameCpp == x) ||
                    depStructDeps.Any(d => d.NameCpp == x) ||
                    depEnumDeps.Any(d => d.NameCpp == x));

                var itemCyclicStr = string.Join(", ", itemCyclic);
                var dependencyCyclicStr = string.Join(", ", dependencyCyclic);

                if (/*string.IsNullOrWhiteSpace(itemCyclicStr) &&*/ string.IsNullOrWhiteSpace(dependencyCyclicStr))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Resolved cyclic dependency: " + ((IEnginePackage)item).Name + " <-> " + ((IEnginePackage)dependency).Name);
                    // for the final sort
                    ((IEnginePackage)item).Dependencies.Clear();
                    ((IEnginePackage)item).Classes.ForEach(x => x.Dependencies.Clear());
                    ((IEnginePackage)item).Structs.ForEach(x => x.Dependencies.Clear());

                    ((IEnginePackage)dependency).Dependencies.Clear();
                    ((IEnginePackage)dependency).Classes.ForEach(x => x.Dependencies.Clear());
                    ((IEnginePackage)dependency).Structs.ForEach(x => x.Dependencies.Clear());
                    cycleList.Remove(item);
                    // cycleList.Remove(dependency);
                    break;
                }

                //if (string.IsNullOrWhiteSpace(itemCyclicStr))
                //    continue;

                //if (string.IsNullOrWhiteSpace(dependencyCyclicStr))
                //    continue;

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[PACKAGE VISITOR] Cyclic Dependency found: " + ((IEnginePackage)item).Name + " <-> " + ((IEnginePackage)dependency).Name);
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine(((IEnginePackage)item).Name + " requires: " + itemCyclicStr);
                Console.WriteLine(((IEnginePackage)dependency).Name + " requires: " + dependencyCyclicStr);
                Console.WriteLine();

                //itemClassDeps.ForEach(c => c.BeforePrint.Add("// CYCLIC DEPENDENCY, FROM " + ((IEnginePackage)dependency).Name));
                //itemStructDeps.ForEach(c => c.BeforePrint.Add("// CYCLIC DEPENDENCY, FROM " + ((IEnginePackage)dependency).Name));
                //itemEnumDeps.ForEach(c => c.BeforePrint.Add("// CYCLIC DEPENDENCY, FROM " + ((IEnginePackage)dependency).Name));

                //((IEnginePackage)item).Classes.AddRange(itemClassDeps);
                //((IEnginePackage)item).Structs.AddRange(itemStructDeps);
                //((IEnginePackage)item).Enums.AddRange(itemEnumDeps);

                //// TODO: the .Any() conditions can probably be replaced with itemCyclicStr.Contains(x)
                //((IEnginePackage)dependency).Classes.RemoveAll(x => itemClassDeps.Any(d => d.NameCpp == x.NameCpp));
                //((IEnginePackage)dependency).Structs.RemoveAll(x => itemStructDeps.Any(d => d.NameCpp == x.NameCpp));
                //((IEnginePackage)dependency).Enums.RemoveAll(x => itemEnumDeps.Any(d => d.NameCpp == x.NameCpp));

                depClassDeps.ForEach(c => c.BeforePrint.Add("// CYCLIC DEPENDENCY, FROM " + ((IEnginePackage)item).Name));
                depStructDeps.ForEach(c => c.BeforePrint.Add("// CYCLIC DEPENDENCY, FROM " + ((IEnginePackage)item).Name));
                depEnumDeps.ForEach(c => c.BeforePrint.Add("// CYCLIC DEPENDENCY, FROM " + ((IEnginePackage)item).Name));

                ((IEnginePackage)dependency).Classes.AddRange(depClassDeps);
                ((IEnginePackage)dependency).Structs.AddRange(depStructDeps);
                ((IEnginePackage)dependency).Enums.AddRange(depEnumDeps);

                // TODO: the .Any() conditions can probably be replaced with dependencyCyclicStr.Contains(x)
                ((IEnginePackage)item).Classes.RemoveAll(x => depClassDeps.Any(d => d.NameCpp == x.NameCpp));
                ((IEnginePackage)item).Structs.RemoveAll(x => depStructDeps.Any(d => d.NameCpp == x.NameCpp));
                ((IEnginePackage)item).Enums.RemoveAll(x => depEnumDeps.Any(d => d.NameCpp == x.NameCpp));

                // for the next sort
                //((IEnginePackage)item).Dependencies.Clear();
                //((IEnginePackage)item).Classes.ForEach(x => x.Dependencies.Clear());
                //((IEnginePackage)item).Structs.ForEach(x => x.Dependencies.Clear());

                //((IEnginePackage)dependency).Dependencies.Clear();
                //((IEnginePackage)dependency).Classes.ForEach(x => x.Dependencies.Clear());
                //((IEnginePackage)dependency).Structs.ForEach(x => x.Dependencies.Clear());

                ((IEnginePackage)item).Dependencies.RemoveAll(x => itemCyclicStr.Contains(x));
                ((IEnginePackage)item).Classes.ForEach(x => x.Dependencies.RemoveAll(d => itemCyclicStr.Contains(d.NameCpp)));
                ((IEnginePackage)item).Structs.ForEach(x => x.Dependencies.RemoveAll(d => itemCyclicStr.Contains(d.NameCpp)));

                ((IEnginePackage)dependency).Dependencies.RemoveAll(x => dependencyCyclicStr.Contains(x));
                ((IEnginePackage)dependency).Classes.ForEach(x => x.Dependencies.RemoveAll(d => itemCyclicStr.Contains(d.NameCpp)));
                ((IEnginePackage)dependency).Structs.ForEach(x => x.Dependencies.RemoveAll(d => itemCyclicStr.Contains(d.NameCpp)));

                if (!cycleList.ContainsKey(item))
                    cycleList.Add(item, dependency);

                //yea = !string.IsNullOrWhiteSpace(itemCyclicStr);
                //if (yea)
                //    Visit(item, getDependencies, sorted, cycleList, visited);
                // cycleList.Add(dependency);
                // break;
            }

            


            visited[item] = false;

            if (!cycleList.ContainsKey(item) && !sorted.Contains(item))
            // if (!sorted.Contains(item))
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
                IEnumerable<string> memDependencies = PluginUtils.GetDependencyCppNameFromMembers(ss.Fields).Concat(PluginUtils.GetDependencyCppNameFromMethods(ss.Methods));
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
            // classList.Reverse();
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
            // structList.Reverse();
            pack.Structs.Clear();
            pack.Structs.AddRange(structList);
        }
    }
}