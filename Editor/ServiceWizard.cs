﻿// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using RealityCollective.Extensions;
using RealityCollective.ServiceFramework.Definitions;
using RealityCollective.ServiceFramework.Editor.Utilities;
using RealityCollective.ServiceFramework.Interfaces;
using RealityCollective.ServiceFramework.Providers;
using RealityCollective.ServiceFramework.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Assembly = System.Reflection.Assembly;

namespace RealityCollective.ServiceFramework.Editor
{
    public class ServiceWizard : EditorWindow
    {
        private const float MIN_VERTICAL_SIZE = 192f;
        private const float MIN_HORIZONTAL_SIZE = 384f;

        private const string TABS = "        ";
        private const string NAME = "#NAME#";
        private const string BASE = "#BASE#";
        private const string GUID = "#GUID#";
        private const string USING = "#USING#";
        private const string PROFILE = "#PROFILE#";
        private const string NAMESPACE = "#NAMESPACE#";
        private const string INTERFACE = "#INTERFACE#";
        private const string IMPLEMENTS = "#IMPLEMENTS#";
        private const string PARENT_INTERFACE = "#PARENT_INTERFACE#";

        private static ServiceWizard window = null;

        // https://stackoverflow.com/questions/6402864/c-pretty-type-name-function
        [SuppressMessage("ReSharper", "BuiltInTypeReferenceStyle")]
        private static readonly Dictionary<string, string> BuildInTypeMap = new Dictionary<string, string>
        {
            { "Void", "void" },
            { "Boolean", "bool" },
            { "Byte", "byte" },
            { "Char", "char" },
            { "Decimal", "decimal" },
            { "Double", "double" },
            { "Single", "float" },
            { "Int32", "int" },
            { "Int64", "long" },
            { "SByte", "sbyte" },
            { "Int16", "short" },
            { "String", "string" },
            { "UInt32", "uint" },
            { "UInt64", "ulong" },
            { "UInt16", "ushort" }
        };

        private Type interfaceType;
        private Type profileBaseType;
        private Type instanceBaseType;

        private string profileTemplatePath;
        private string instanceTemplatePath;
        private string interfaceTemplatePath;
        private string outputPath = string.Empty;
        private string @namespace = string.Empty;
        private string @parentInterfaceName = string.Empty;
        private string instanceName = string.Empty;
        private Type parentInterfaceType = null;

        public static void ShowNewServiceWizard(Type interfaceType)
        {
            if (window != null)
            {
                window.Close();
            }

            if (interfaceType == null)
            {
                Debug.LogError($"{nameof(interfaceType)} was null");
                return;
            }

            var templatePath = $"{ServiceFrameworkFinderUtility.AbsoluteFolderPath}\\Editor\\Templates~"; ;

            window = CreateInstance<ServiceWizard>();
            window.minSize = new Vector2(MIN_HORIZONTAL_SIZE, MIN_VERTICAL_SIZE);
            window.maxSize = new Vector2(MIN_HORIZONTAL_SIZE, MIN_VERTICAL_SIZE);
            window.position = new Rect(0f, 0f, MIN_HORIZONTAL_SIZE, MIN_VERTICAL_SIZE);
            window.titleContent = new GUIContent("Service Wizard");
            window.interfaceType = interfaceType;

            switch (interfaceType)
            {
                case Type _ when typeof(IServiceDataProvider).IsAssignableFrom(interfaceType):
                    window.profileTemplatePath = $"{templatePath}\\DataProviderProfile.txt";
                    window.instanceTemplatePath = $"{templatePath}\\DataProvider.txt";
                    window.interfaceTemplatePath = $"{templatePath}\\IServiceDataProvider.txt";
                    window.instanceBaseType = typeof(BaseServiceDataProvider);
                    window.profileBaseType = typeof(BaseProfile);
                    break;
                case Type _ when typeof(IService).IsAssignableFrom(interfaceType):
                    window.profileTemplatePath = $"{templatePath}\\ServiceProfile.txt";
                    window.instanceTemplatePath = $"{templatePath}\\Service.txt";
                    window.interfaceTemplatePath = $"{templatePath}\\IService.txt";
                    window.instanceBaseType = typeof(BaseServiceWithConstructor);
                    window.profileBaseType = typeof(BaseServiceProfile<>);
                    break;
                default:
                    Debug.LogError($"{interfaceType.Name} does not implement {nameof(IService)}");
                    return;
            }

            window.ShowUtility();
        }

        private void OnGUI()
        {
            if (interfaceType == null)
            {
                Close();
                return;
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = Application.dataPath;
                @namespace = $"{Application.productName}";
            }

            var interfaceStrippedName = interfaceType.Name.Replace("I", string.Empty);

            if (string.IsNullOrWhiteSpace(instanceName))
            {
                instanceName = interfaceStrippedName;
            }

            if (string.IsNullOrWhiteSpace(@parentInterfaceName))
            {
                @parentInterfaceName = interfaceType.Name.Replace("DataProvider", string.Empty);
            }

            GUILayout.BeginVertical();

            GUILayout.Label($"Let's create a {interfaceType.Name}!", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField(new GUIContent($"Choose a path to put your new {Path.GetFileNameWithoutExtension(instanceTemplatePath).ToProperCase()}:"));
            const int maxCharacterLength = 56;
            EditorGUILayout.TextField(outputPath, new GUIStyle("label")
            {
                alignment = outputPath.Length > maxCharacterLength
                    ? TextAnchor.MiddleRight
                    : TextAnchor.MiddleLeft
            });

            if (GUILayout.Button("Choose the output path"))
            {
                outputPath = EditorUtility.OpenFolderPanel("Generation Location", outputPath, string.Empty);
            }

            EditorGUILayout.Space();
            @namespace = EditorGUILayout.TextField("Namespace", @namespace);
            if (@namespace.Contains('-'))
            {
                @namespace = @namespace.Replace("-", string.Empty);
            }

            if (interfaceType.Name.Contains("DataProvider"))
            {
                @parentInterfaceName = EditorGUILayout.TextField("Parent Service Interface", @parentInterfaceName);
                //parentInterfaceType = GetType($"{@parentInterfaceName}");
                //if (parentInterfaceType == null)
                //{
                //    EditorGUILayout.TextField("Parent Interface not found");
                //    return;
                //}
            }

            EditorGUI.BeginChangeCheck();
            instanceName = EditorGUILayout.TextField("Instance Name", instanceName);

            GUILayout.FlexibleSpace();

            GUI.enabled = !string.IsNullOrWhiteSpace(instanceName) && !string.IsNullOrWhiteSpace(@namespace);

            if (GUILayout.Button("Generate!"))
            {
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        if (@namespace.Contains('-'))
                        {
                            @namespace = @namespace.Replace("-", string.Empty);
                        }

                        if (interfaceType.Name.Contains("DataProvider"))
                        {
                            parentInterfaceType = GetType($"{@parentInterfaceName}");
                            if (parentInterfaceType == null)
                            {
                                Debug.Log("Parent Interface not found");
                                return;
                            }
                        }

                        var interfaceName = $"I{instanceName}";

                        var usingList = new List<string>();

                        GenerateInterface(interfaceName, usingList);

                        if (!usingList.Contains(instanceBaseType.Namespace))
                        {
                            usingList.Add(instanceBaseType.Namespace);
                        }

                        if (!usingList.Contains(interfaceType.Namespace))
                        {
                            usingList.Add(interfaceType.Namespace);
                        }


                        if (interfaceType.Name.Contains("DataProvider"))
                        {
                            if (parentInterfaceType != null)
                            {
                                if (!usingList.Contains(parentInterfaceType.Namespace))
                                {
                                    usingList.Add(parentInterfaceType.Namespace);
                                }
                            }
                            else
                            {
                                Debug.LogError($"Failed to resolve parent interface for {interfaceType.Name}");
                            }
                        }

                        var implements = string.Empty;

                        var members = interfaceType.GetMembers();
                        var events = new List<EventInfo>();
                        var properties = new List<PropertyInfo>();
                        var methods = new List<MethodInfo>();

                        foreach (var memberInfo in members)
                        {
                            switch (memberInfo)
                            {
                                case EventInfo eventInfo:
                                    events.Add(eventInfo);
                                    break;
                                case PropertyInfo propertyInfo:
                                    properties.Add(propertyInfo);
                                    break;
                                case MethodInfo methodInfo:
                                    methods.Add(methodInfo);
                                    break;
                            }
                        }

                        implements = events.Aggregate(implements, (current, eventInfo) => $"{current}{FormatMemberInfo(eventInfo, ref usingList)}");
                        implements = properties.Aggregate(implements, (current, propertyInfo) => $"{current}{FormatMemberInfo(propertyInfo, ref usingList)}");
                        implements = methods.Aggregate(implements, (current, methodInfo) => $"{current}{FormatMemberInfo(methodInfo, ref usingList)}");

                        Type profileType = null;
                        var profileBaseTypeName = profileBaseType.Name;

                        if (profileBaseTypeName.Contains("`1"))
                        {
                            var dataProviderInterfaceTypeName = interfaceType.Name
                                .Replace("Service", "ServiceDataProvider");

                            var dataProviderType = GetType(dataProviderInterfaceTypeName);

                            if (dataProviderType != null)
                            {
                                if (!usingList.Contains(dataProviderType.Namespace))
                                {
                                    usingList.Add(dataProviderType.Namespace);
                                }

                                var constructors = dataProviderType.GetConstructors();

                                foreach (var constructorInfo in constructors)
                                {
                                    var parameters = constructorInfo.GetParameters();

                                    foreach (var parameterInfo in parameters)
                                    {
                                        if (parameterInfo.ParameterType.IsAbstract) { continue; }

                                        if (parameterInfo.ParameterType.IsSubclassOf(typeof(BaseProfile)))
                                        {
                                            profileType = parameterInfo.ParameterType;
                                            break;
                                        }
                                    }

                                    if (profileType != null)
                                    {
                                        profileBaseTypeName = profileType.Name;
                                        break;
                                    }
                                }

                            }

                            profileBaseTypeName = profileBaseTypeName.Replace("`1", $"<{dataProviderInterfaceTypeName}>");
                        }

                        GenerateService(interfaceName, usingList, parentInterfaceType, implements, profileBaseTypeName);

                        if (profileBaseTypeName != nameof(BaseProfile))
                        {
                            GenerateProfile(profileBaseTypeName, usingList);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError(e);
                    }
                    finally
                    {
                        Close();
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    }
                };
            }

            GUI.enabled = true;
            EditorGUILayout.Space();
            GUILayout.EndVertical();
        }

        private void GenerateInterface(string newInterfaceName, List<string> usingList)
        {
            usingList.Clear();

            usingList.EnsureListItem("RealityCollective.ServiceFramework.Interfaces");

            var @using = usingList.Aggregate(string.Empty, (current, item) => $"{current}{Environment.NewLine}using {item};");

            var interfaceTemplate = File.ReadAllText(interfaceTemplatePath ?? throw new InvalidOperationException());
            interfaceTemplate = interfaceTemplate.Replace(USING, @using);
            interfaceTemplate = interfaceTemplate.Replace(NAMESPACE, @namespace);
            interfaceTemplate = interfaceTemplate.Replace(INTERFACE, newInterfaceName);
            interfaceTemplate = interfaceTemplate.Replace(NAME, instanceName);

            File.WriteAllText($"{outputPath}/{newInterfaceName}.cs", interfaceTemplate);
        }

        private string GenerateService(string newInterfaceName, List<string> usingList, Type parentInterfaceType, string implements, string profileBaseTypeName)
        {
            usingList.EnsureListItem(profileBaseType.Namespace);

            usingList.Sort();

            var @using = usingList.Aggregate(string.Empty, (current, item) => $"{current}{Environment.NewLine}using {item};");

            var instanceTemplate = File.ReadAllText(instanceTemplatePath ?? throw new InvalidOperationException());
            instanceTemplate = instanceTemplate.Replace(USING, @using);
            instanceTemplate = instanceTemplate.Replace(NAMESPACE, @namespace);
            instanceTemplate = instanceTemplate.Replace(GUID, Guid.NewGuid().ToString());
            instanceTemplate = instanceTemplate.Replace(NAME, instanceName);
            instanceTemplate = instanceTemplate.Replace(BASE, instanceBaseType.Name);
            instanceTemplate = instanceTemplate.Replace(INTERFACE, newInterfaceName);// interfaceType.Name);
            instanceTemplate = instanceTemplate.Replace(PARENT_INTERFACE, parentInterfaceType?.Name);
            instanceTemplate = instanceTemplate.Replace(IMPLEMENTS, implements);
            instanceTemplate = instanceTemplate.Replace(PROFILE, profileBaseTypeName);

            var fileName = $"{instanceName}.cs";
            File.WriteAllText($"{outputPath}/{fileName}", instanceTemplate);
            return @using;
        }

        private void GenerateProfile(string profileBaseTypeName, List<string> usingList)
        {
            usingList.Clear();

            usingList.EnsureListItem(profileBaseType.Namespace);
            usingList.EnsureListItem("RealityCollective.ServiceFramework.Interfaces");
            usingList.EnsureListItem("UnityEngine");

            usingList.Sort();

            var @using = usingList.Aggregate(string.Empty, (current, item) => $"{current}{Environment.NewLine}using {item};");

            var profileTemplate = File.ReadAllText(profileTemplatePath ?? throw new InvalidOperationException());
            profileTemplate = profileTemplate.Replace(USING, @using);
            profileTemplate = profileTemplate.Replace(NAMESPACE, @namespace);
            profileTemplate = profileTemplate.Replace(NAME, instanceName);
            profileTemplate = profileTemplate.Replace(BASE, profileBaseTypeName);

            File.WriteAllText($"{outputPath}/{instanceName}Profile.cs", profileTemplate);
        }

        private static string FormatMemberInfo(MemberInfo memberInfo, ref List<string> usingList)
        {
            var result = $"{Environment.NewLine}{Environment.NewLine}{TABS}/// <inheritdoc />{Environment.NewLine}{TABS}";

            switch (memberInfo)
            {
                case EventInfo eventInfo:
                    result += $"public event {PrettyPrintTypeName(eventInfo.EventHandlerType, ref usingList)} {eventInfo.Name};";
                    break;
                case PropertyInfo propertyInfo:
                    if (propertyInfo.Name == "Name" || propertyInfo.Name == "Priority")
                    {
                        return string.Empty;
                    }
                    var getter = propertyInfo.CanRead ? " get;" : string.Empty;
                    var setter = propertyInfo.CanWrite ? " set;" : string.Empty;

                    if (!usingList.Contains(propertyInfo.PropertyType.Namespace))
                    {
                        usingList.Add(propertyInfo.PropertyType.Namespace);
                    }

                    result += $"public override {PrettyPrintTypeName(propertyInfo.PropertyType, ref usingList)} {propertyInfo.Name} {{{getter}{setter} }}";
                    break;
                case MethodInfo methodInfo:
                    if (methodInfo.Name.Contains("get_") ||
                        methodInfo.Name.Contains("set_") ||
                        methodInfo.Name.Contains("add_") ||
                        methodInfo.Name.Contains("remove_"))
                    {
                        return string.Empty;
                    }

                    if (!usingList.Contains(methodInfo.ReturnType.Namespace))
                    {
                        usingList.Add(methodInfo.ReturnType.Namespace);
                    }

                    var returnTypeName = PrettyPrintTypeName(methodInfo.ReturnType, ref usingList);
                    var parameters = string.Empty;
                    var parameterList = new List<string>();

                    foreach (var parameterInfo in methodInfo.GetParameters())
                    {
                        var isByRef = parameterInfo.ParameterType.IsByRef
                            ? parameterInfo.IsOut
                                ? "out "
                                : "ref "
                            : string.Empty;
                        //var isOptional = parameterInfo.IsOptional
                        //    ? $" = {PrettyPrintTypeName(parameterInfo.GetOptionalCustomModifiers()[0], ref usingList)}"
                        //    : string.Empty;
                        parameterList.Add($"{isByRef}{PrettyPrintTypeName(parameterInfo.ParameterType, ref usingList)} {parameterInfo.Name}");
                    }

                    for (var i = 0; i < parameterList.Count; i++)
                    {
                        var isLast = i + 1 == parameterList.Count;
                        var comma = isLast ? string.Empty : ", ";
                        parameters += $"{parameterList[i]}{comma}";
                    }

                    result += $"public override {returnTypeName} {methodInfo.Name}({parameters}){Environment.NewLine}{TABS}{{{Environment.NewLine}{TABS}    throw new NotImplementedException();{Environment.NewLine}{TABS}}}";
                    break;
                default:
                    Debug.LogWarning($"Unhandled {nameof(memberInfo)}\n{memberInfo}");
                    result += $"{memberInfo}";
                    break;
            }

            return result;
        }

        private static string PrettyPrintTypeName(Type type, ref List<string> usingList)
        {
            string typeName;

            if (BuildInTypeMap.ContainsKey(type.Name))
            {
                typeName = BuildInTypeMap[type.Name];
            }
            else
            {
                if (!usingList.Contains(type.Namespace))
                {
                    usingList.Add(type.Namespace);
                }

                typeName = type.IsByRef
                    ? PrettyPrintTypeName(type.GetElementType(), ref usingList)
                    : type.Name;

                if (type.IsNested)
                {
                    typeName = type.FullName?
                        .Replace($"{type.Namespace}.", string.Empty)
                        .Replace("+", ".");
                }
            }

            if (!type.IsGenericType)
            {
                return typeName;
            }

            var genericArguments = type.GetGenericArguments();

            if (genericArguments.Length == 0)
            {
                return typeName;
            }

            if (type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return $"{PrettyPrintTypeName(Nullable.GetUnderlyingType(type), ref usingList)}?";
            }

            var genericTypeNames = new List<string>();

            foreach (var genericType in genericArguments)
            {
                var name = PrettyPrintTypeName(genericType, ref usingList);

                if (!genericTypeNames.Contains(name))
                {
                    genericTypeNames.Add(name);
                }
            }

            var mangledName = typeName.Contains("`")
                ? typeName.Substring(0, typeName.IndexOf("`", StringComparison.Ordinal))
                : typeName;
            return $"{mangledName}<{string.Join(",", genericTypeNames)}>";
        }

        private static Type GetType(string name) => GetTypes().FirstOrDefault(type => type.Name == name);

        private static IEnumerable<Type> GetTypes()
        {
            var types = new List<Type>();
            var assemblies = CompilationPipeline.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                var compiledAssembly = Assembly.Load(assembly.name);
                types.AddRange(compiledAssembly.ExportedTypes);
            }

            types.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
            return types;
        }

        const string createNewServiceMenuItemName = ServiceFrameworkPreferences.Editor_Menu_Keyword + "/ServiceGenerator/Create new service";

        [MenuItem(createNewServiceMenuItemName)]
        private static void CreateNewService()
        {
            ServiceWizard.ShowNewServiceWizard(typeof(IService));
        }

        const string createNewDataProviderMenuItemName = ServiceFrameworkPreferences.Editor_Menu_Keyword + "/ServiceGenerator/Create new data provider";

        [MenuItem(createNewDataProviderMenuItemName)]
        private static void CreateNewDataProvider()
        {
            ServiceWizard.ShowNewServiceWizard(typeof(IServiceDataProvider));
        }
    }
}
