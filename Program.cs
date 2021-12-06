using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Tooling.Connector;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

namespace dnmx.EntityCodeGenerator
{
    public class Program
    {
        public const string DEFAULT_ENUM_NAMESPACE = "Enum";
        public const string DEFAULT_UI_NAMESPACE = "UI";
        public const string DEFAULT_FORM_NAMESPACE = "Form";
        public const string DEFAULT_SERVICECONTEXT_NAME = "ServiceContext";
        public const string COMMENT_PREFIX = "//";
        public const string CLASS_ID_PROPERTYNAME = "ID";
        public const string CLASS_ID_OVERRIDE_PROPERTYNAME = "Id";
        public const string CLASS_CONSTRUCTOR_ID_PARAMETERNAME = "id";
        public const string CLASS_CONSTRUCTOR_GETPF_PROPERTYNAME = "GetPF";
        public const string CLASS_CONSTRUCTOR_XRMAPP_PARAMETERNAME = "xrmApp";
        public const string CLASS_PF_PROPERTYNAME = "PF";
        public const string CLASS_FORM_PROPERTYNAME = "Form";

        public const string PROPERTYCHANGING_NAME = "PropertyChanging";
        public const string PROPERTYCHANGED_NAME = "PropertyChanged";
        public const string ONPROPERTYCHANGING_NAME = "OnPropertyChanging";
        public const string ONPROPERTYCHANGED_NAME = "OnPropertyChanged";

        static Regex classNameRegex = new Regex("(a|e|i|o|u|A|E|I|O|U)$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static Dictionary<int, EntityMetadata> entityMetadataCache = new Dictionary<int, EntityMetadata>();

        public static void Main(string[] args)
        {
            string connectionString = null;
            string ns = string.Empty;
            string[] entityLogicalNames = Array.Empty<string>();
            string[] uiEntityLogicalNames = Array.Empty<string>();
            string rootDir = Directory.GetCurrentDirectory();

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i].Trim().ToLower();

                if (arg.StartsWith("/"))
                {
                    switch (arg)
                    {
                        case "/connectionstring":
                            connectionString = args[i++ + 1].Trim();
                            continue;
                        case "/namespace":
                            ns = args[i++ + 1].Trim();
                            continue;
                        case "/entities":
                            entityLogicalNames = args[i++ + 1].Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

                            for (int j = 0; j < entityLogicalNames.Length; j++)
                            {
                                entityLogicalNames[j] = entityLogicalNames[j].Trim();
                            }

                            continue;
                        case "/uientities":
                            uiEntityLogicalNames = args[i++ + 1].Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

                            for (int j = 0; j < uiEntityLogicalNames.Length; j++)
                            {
                                uiEntityLogicalNames[j] = uiEntityLogicalNames[j].Trim();
                            }

                            continue;
                        case "/output":
                            rootDir = args[i++ + 1].Trim();
                            continue;
                    }
                }

                throw new Exception($"Unrecognized argument: '{arg}'.");
            }

            if (connectionString is null)
            {
                throw new Exception($"Connection string is required.");
            }

            if (!string.IsNullOrWhiteSpace(ns))
            {
                rootDir = Path.Combine(rootDir, ns);
            }



            var serviceContextName = DEFAULT_SERVICECONTEXT_NAME;
            var nsEnum = DEFAULT_ENUM_NAMESPACE;
            var nsUI = DEFAULT_UI_NAMESPACE;
            var nsForm = DEFAULT_FORM_NAMESPACE;


            var overwriteExisting = true;
            var remarked = false;



            //==========================================================================



            using (var service = new CrmServiceClient(connectionString))
            {
                if (service.IsReady)
                {
                    string prefix = string.Empty;

                    if (remarked)
                    {
                        prefix = COMMENT_PREFIX;
                    }

                    if (string.IsNullOrWhiteSpace(nsEnum))
                    {
                        nsEnum = DEFAULT_ENUM_NAMESPACE;
                    }

                    if (string.IsNullOrWhiteSpace(nsUI))
                    {
                        nsUI = DEFAULT_ENUM_NAMESPACE;
                    }

                    var rootDirInfo = new DirectoryInfo(rootDir);
                    var dirInfo = new DirectoryInfo(Path.Combine(rootDirInfo.FullName, "Entity"));
                    var enumDirInfo = new DirectoryInfo(Path.Combine(dirInfo.FullName, nsEnum));
                    var uiDirInfo = new DirectoryInfo(Path.Combine(dirInfo.FullName, nsUI));
                    var formDirInfo = new DirectoryInfo(Path.Combine(uiDirInfo.FullName, nsForm));

                    if (!dirInfo.Exists)
                    {
                        dirInfo.Create();
                    }

                    if (!enumDirInfo.Exists)
                    {
                        enumDirInfo.Create();
                    }

                    if (uiEntityLogicalNames.Length > 0 &&
                        !uiDirInfo.Exists)
                    {
                        uiDirInfo.Create();
                    }

                    if (uiEntityLogicalNames.Length > 0 &&
                        !formDirInfo.Exists)
                    {
                        formDirInfo.Create();
                    }



                    List<Tuple<string, string, bool, EntityMetadata>> entityClasses = new List<Tuple<string, string, bool, EntityMetadata>>();

                    foreach (var entityLogicalName in entityLogicalNames)
                    {
                        var request = new RetrieveEntityRequest()
                        {
                            LogicalName = entityLogicalName,
                            //EntityFilters = EntityFilters.Relationships | EntityFilters.Attributes,
                            EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
                            RetrieveAsIfPublished = false,
                        };

                        var response = service.Execute(request) as RetrieveEntityResponse;
                        var entityMetadata = response?.EntityMetadata;

                        #region Enum

                        var fileEnumInfo = new FileInfo(Path.Combine(enumDirInfo.FullName, $"{entityLogicalName}.cs"));

                        if (!fileEnumInfo.Exists ||
                            (fileEnumInfo.Exists && overwriteExisting))
                        {
                            using (var baseWriter = new StreamWriter(fileEnumInfo.FullName, false))
                            {
                                using (var writer = new IndentedTextWriter(baseWriter, "\t"))
                                {
                                    if (GenerateEnum(service, writer, ns, nsEnum, entityMetadata, remarked))
                                    {
                                        writer.Flush();
                                    }
                                }
                            }
                        }

                        #endregion

                        #region Entity

                        var fileInfo = new FileInfo(Path.Combine(dirInfo.FullName, $"{entityLogicalName}.cs"));

                        if (!fileInfo.Exists ||
                            (fileInfo.Exists && overwriteExisting))
                        {
                            using (var baseWriter = new StreamWriter(fileInfo.FullName, false))
                            {
                                using (var writer = new IndentedTextWriter(baseWriter, "\t"))
                                {
                                    #region Namespace start

                                    if (!string.IsNullOrWhiteSpace(ns))
                                    {
                                        writer.WriteLine(prefix + $"namespace {ns}");
                                        writer.WriteLine(prefix + "{");
                                        writer.Indent++;
                                    }

                                    #endregion

                                    List<int> propertyNameIndex = new List<int>();

                                    GenerateClass(service, writer, true, ns, nsEnum, entityClasses, propertyNameIndex, entityMetadata, remarked);
                                    writer.WriteLine();
                                    GenerateClass(service, writer, false, ns, nsEnum, entityClasses, propertyNameIndex, entityMetadata, remarked);

                                    #region Namespace end

                                    if (!string.IsNullOrWhiteSpace(ns))
                                    {
                                        writer.Indent--;
                                        writer.WriteLine(prefix + "}");
                                    }

                                    #endregion

                                    writer.Flush();
                                }
                            }
                        }

                        #endregion

                        if (uiEntityLogicalNames.Contains(entityLogicalName))
                        {
                            #region UI Entity

                            var fileUIInfo = new FileInfo(Path.Combine(uiDirInfo.FullName, $"{entityLogicalName}.cs"));

                            if (!fileUIInfo.Exists ||
                                (fileUIInfo.Exists && overwriteExisting))
                            {
                                using (var baseWriter = new StreamWriter(fileUIInfo.FullName, false))
                                {
                                    using (var writer = new IndentedTextWriter(baseWriter, "\t"))
                                    {
                                        #region Namespace start

                                        if (string.IsNullOrWhiteSpace(ns))
                                        {
                                            writer.WriteLine(prefix + $"namespace {nsUI}");
                                        }
                                        else
                                        {
                                            writer.WriteLine(prefix + $"namespace {ns}.{nsUI}");
                                        }

                                        writer.WriteLine(prefix + "{");
                                        writer.Indent++;

                                        #endregion

                                        List<int> propertyNameIndex = new List<int>();

                                        GenerateUIClass(service, writer, true, ns, nsEnum, nsUI, nsForm, propertyNameIndex, entityMetadata, remarked);
                                        writer.WriteLine();
                                        GenerateUIClass(service, writer, false, ns, nsEnum, nsUI, nsForm, propertyNameIndex, entityMetadata, remarked);

                                        #region Namespace end

                                        writer.Indent--;
                                        writer.WriteLine(prefix + "}");

                                        #endregion

                                        writer.Flush();
                                    }
                                }
                            }

                            #endregion

                            #region UI Form

                            var fileFormInfo = new FileInfo(Path.Combine(formDirInfo.FullName, $"{entityLogicalName}.cs"));

                            if (!fileFormInfo.Exists ||
                                (fileFormInfo.Exists && overwriteExisting))
                            {
                                using (var baseWriter = new StreamWriter(fileFormInfo.FullName, false))
                                {
                                    using (var writer = new IndentedTextWriter(baseWriter, "\t"))
                                    {
                                        #region Namespace start

                                        if (string.IsNullOrWhiteSpace(ns))
                                        {
                                            writer.WriteLine(prefix + $"namespace {nsUI}.{nsForm}");
                                        }
                                        else
                                        {
                                            writer.WriteLine(prefix + $"namespace {ns}.{nsUI}.{nsForm}");
                                        }

                                        writer.WriteLine(prefix + "{");
                                        writer.Indent++;

                                        #endregion

                                        List<int> propertyNameIndex = new List<int>();

                                        GenerateUIFormClass(service, writer, true, ns, nsEnum, nsUI, nsForm, propertyNameIndex, entityMetadata, remarked);
                                        writer.WriteLine();
                                        GenerateUIFormClass(service, writer, false, ns, nsEnum, nsUI, nsForm, propertyNameIndex, entityMetadata, remarked);

                                        #region Namespace end

                                        writer.Indent--;
                                        writer.WriteLine(prefix + "}");

                                        #endregion

                                        writer.Flush();
                                    }
                                }
                            }

                            #endregion
                        }
                    }

                    #region ServiceContext

                    if (entityClasses.Count > 0)
                    {
                        entityClasses = entityClasses.OrderBy(entityClass => entityClass.Item1).ToList();

                        var serviceContextFileInfo = new FileInfo(Path.Combine(rootDirInfo.FullName, $"{serviceContextName}.cs"));

                        if (!serviceContextFileInfo.Exists ||
                            (serviceContextFileInfo.Exists && overwriteExisting))
                        {
                            using (var baseWriter = new StreamWriter(serviceContextFileInfo.FullName, false))
                            {
                                using (var writer = new IndentedTextWriter(baseWriter, "\t"))
                                {
                                    writer.WriteLine("[assembly: Microsoft.Xrm.Sdk.Client.ProxyTypesAssemblyAttribute()]");
                                    writer.WriteLine();

                                    if (!string.IsNullOrWhiteSpace(ns))
                                    {
                                        writer.WriteLine(prefix + $"namespace {ns}");
                                        writer.WriteLine(prefix + "{");
                                        writer.Indent++;
                                    }

                                    writer.WriteLine(prefix + $"public partial class {serviceContextName} : dnmx.ServiceContext");
                                    writer.WriteLine(prefix + "{");
                                    writer.Indent++;

                                    writer.WriteLine(prefix + $"public {serviceContextName}(Microsoft.Xrm.Sdk.IOrganizationService service) : base(service)");
                                    writer.WriteLine(prefix + "{");
                                    writer.Indent++;

                                    writer.WriteLine(prefix + "MergeOption = Microsoft.Xrm.Sdk.Client.MergeOption.NoTracking;");
                                    writer.WriteLine(prefix + "SaveChangesDefaultOptions = Microsoft.Xrm.Sdk.Client.SaveChangesOptions.None;");
                                    writer.WriteLine(prefix + "ConcurrencyBehavior = Microsoft.Xrm.Sdk.ConcurrencyBehavior.AlwaysOverwrite;");
                                    writer.WriteLine(prefix + "NormalizeNullValue = false;");
                                    writer.WriteLine(prefix + "NormalizeStandardTypes = false;");

                                    writer.Indent--;
                                    writer.WriteLine(prefix + "}");

                                    foreach (var entityClass in entityClasses)
                                    {
                                        var entityClassPrefix = string.Empty;

                                        if (entityClass.Item3)
                                        {
                                            entityClassPrefix = COMMENT_PREFIX;
                                        }

                                        writer.WriteLine();
                                        GenerateClassSummary(writer, entityClass.Item4, remarked, true);

                                        writer.WriteLine(prefix + entityClassPrefix + $"public System.Linq.IQueryable<{entityClass.Item1}> {entityClass.Item2}");
                                        writer.WriteLine(prefix + entityClassPrefix + "{");
                                        writer.Indent++;

                                        writer.WriteLine(prefix + entityClassPrefix + "get");
                                        writer.WriteLine(prefix + entityClassPrefix + "{");
                                        writer.Indent++;

                                        writer.WriteLine(prefix + entityClassPrefix + $"return CreateQuery<{entityClass.Item1}>();");

                                        writer.Indent--;
                                        writer.WriteLine(prefix + entityClassPrefix + "}");

                                        writer.Indent--;
                                        writer.WriteLine(prefix + entityClassPrefix + "}");
                                    }

                                    writer.Indent--;
                                    writer.WriteLine(prefix + "}");

                                    if (!string.IsNullOrWhiteSpace(ns))
                                    {
                                        writer.Indent--;
                                        writer.WriteLine(prefix + "}");
                                    }

                                    writer.Flush();
                                }
                            }
                        }
                    }

                    #endregion
                }
            }
        }

        private static void GenerateClass(IOrganizationService service, IndentedTextWriter writer, bool useLogicalName, string ns, string nsEnum, List<Tuple<string, string, bool, EntityMetadata>> entityClasses, List<int> propertyNameIndex, EntityMetadata entityMetadata, bool remarked)
        {
            var isUI = false;
            var originalRemarked = remarked;
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            if (TryGetClassName(useLogicalName, entityMetadata, out string className))
            {
                if (useLogicalName)
                {
                    var entityCollectionName = CreateValidIdentifier(entityMetadata.DisplayCollectionName?.UserLocalizedLabel?.Label);

                    if (string.IsNullOrWhiteSpace(entityCollectionName))
                    {
                        if (classNameRegex.IsMatch(className))
                        {
                            entityCollectionName = $"{className}s";
                        }
                        else
                        {
                            entityCollectionName = $"{className}es";
                        }
                    }

                    entityClasses.Add(new Tuple<string, string, bool, EntityMetadata>(className, entityCollectionName, originalRemarked, entityMetadata));
                }

                if (useLogicalName)
                {
                    GenerateClassSummary(writer, entityMetadata, remarked);
                    GenerateClassAttributes(writer, useLogicalName, entityMetadata, remarked);
                }

                GenerateClassDeclaration(writer, useLogicalName, className, remarked);

                #region Class start

                writer.WriteLine(prefix + "{");
                writer.Indent++;

                #endregion

                //if (useLogicalName)
                //{
                //	GeneratePropertyEventHandler(writer, remarked);
                //}

                #region Attributes

                if (useLogicalName)
                {
                    propertyNameIndex.Add(className.GetHashCode());

                    //propertyNameIndex.Add(PROPERTYCHANGING_NAME.GetHashCode());
                    //propertyNameIndex.Add(PROPERTYCHANGED_NAME.GetHashCode());
                    //propertyNameIndex.Add(ONPROPERTYCHANGING_NAME.GetHashCode());
                    //propertyNameIndex.Add(ONPROPERTYCHANGED_NAME.GetHashCode());

                    #region Id

                    GenerateIdOverride(writer, entityMetadata.PrimaryIdAttribute, CLASS_ID_OVERRIDE_PROPERTYNAME, remarked);
                    propertyNameIndex.Add(CLASS_ID_OVERRIDE_PROPERTYNAME.GetHashCode());

                    #endregion

                    #region ID

                    var primaryIdAttributeMetadata = entityMetadata.Attributes.Where(am => am.LogicalName == entityMetadata.PrimaryIdAttribute).First();

                    writer.WriteLine();
                    GenerateIdProperty(service, writer, entityMetadata, primaryIdAttributeMetadata, CLASS_ID_PROPERTYNAME, remarked);
                    propertyNameIndex.Add(CLASS_ID_PROPERTYNAME.GetHashCode());

                    #endregion

                    #region PF

                    var primaryNameAttributeMetadata = entityMetadata.Attributes.Where(am => am.LogicalName == entityMetadata.PrimaryNameAttribute).First();

                    writer.WriteLine();
                    GenerateStringProperty(service, writer, entityMetadata, primaryNameAttributeMetadata, CLASS_PF_PROPERTYNAME, remarked, isUI, true, false);
                    propertyNameIndex.Add(CLASS_PF_PROPERTYNAME.GetHashCode());

                    #endregion

                    writer.WriteLine();
                    GenerateClassConstructor(writer, ns, entityMetadata, className, remarked);

                    writer.WriteLine();
                    writer.WriteLine(prefix + $"protected override string {CLASS_CONSTRUCTOR_GETPF_PROPERTYNAME}() {{ return this.{CLASS_PF_PROPERTYNAME}; }}");
                    propertyNameIndex.Add(CLASS_CONSTRUCTOR_GETPF_PROPERTYNAME.GetHashCode());

                    //var baseType = typeof(Entity);
                    var baseType = typeof(AbstractEntity);
                    var bindingFlags = BindingFlags.FlattenHierarchy | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                    foreach (var memberInfo in baseType.GetProperties(bindingFlags))
                    {
                        if (memberInfo.GetIndexParameters().Length > 0)
                        {
                            continue;
                        }

                        if (!IsValidProperty(memberInfo.GetMethod) &&
                            !IsValidProperty(memberInfo.SetMethod))
                        {
                            continue;
                        }

                        var memberInfoNameHashCode = memberInfo.Name.GetHashCode();

                        if (!propertyNameIndex.Contains(memberInfoNameHashCode))
                        {
                            propertyNameIndex.Add(memberInfoNameHashCode);
                        }
                    }

                    foreach (var memberInfo in baseType.GetFields(bindingFlags))
                    {
                        if (!IsValidField(memberInfo))
                        {
                            continue;
                        }

                        var memberInfoNameHashCode = memberInfo.Name.GetHashCode();

                        if (!propertyNameIndex.Contains(memberInfoNameHashCode))
                        {
                            propertyNameIndex.Add(memberInfoNameHashCode);
                        }
                    }

                    foreach (var memberInfo in baseType.GetMethods(bindingFlags))
                    {
                        if (!IsValidMethod(memberInfo))
                        {
                            continue;
                        }

                        var memberInfoNameHashCode = memberInfo.Name.GetHashCode();

                        if (!propertyNameIndex.Contains(memberInfoNameHashCode))
                        {
                            propertyNameIndex.Add(memberInfoNameHashCode);
                        }
                    }
                }

                AttributeMetadataWithPropertyName[] attributeMetadataWithPropertyList = entityMetadata.Attributes.Select(am =>
                {
                    TryGetPropertyName(useLogicalName, am, out string propertyName);

                    return new AttributeMetadataWithPropertyName
                    {
                        PropertyName = propertyName,
                        Value = am
                    };
                })
                    .OrderBy(am => am, new ClassPropertyNameComparer())
                    .ToArray();

                for (int i = 0; i < attributeMetadataWithPropertyList.Length; i++)
                {
                    remarked = originalRemarked;

                    if (useLogicalName ||
                        i > 0)
                    {
                        writer.WriteLine();
                    }

                    var attributeMetadataWithProperty = attributeMetadataWithPropertyList[i];
                    var propertyName = attributeMetadataWithProperty.PropertyName;
                    var attributeMetadata = attributeMetadataWithProperty.Value;

                    if (attributeMetadata.AttributeType.HasValue)
                    {
                        if (attributeMetadata.AttributeType.Value == AttributeTypeCode.PartyList ||
                            attributeMetadata.AttributeType.Value == AttributeTypeCode.CalendarRules ||
                            attributeMetadata.AttributeType.Value == AttributeTypeCode.Virtual ||
                            attributeMetadata.AttributeType.Value == AttributeTypeCode.EntityName)
                        {
                            GenerateUnsupportedProperty(writer, attributeMetadata, remarked);
                        }
                        else
                        {
                            if (useLogicalName ||
                                !string.IsNullOrWhiteSpace(propertyName))
                            {
                                if (attributeMetadata.AttributeType.Value == AttributeTypeCode.BigInt ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Boolean ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.DateTime ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Decimal ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Double ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Integer ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Lookup ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Customer ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Owner ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.ManagedProperty ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Money ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Picklist ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.State ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Status ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.String ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Memo ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Uniqueidentifier ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Virtual)
                                {
                                    var propertyNameHashCode = propertyName.GetHashCode();

                                    if (propertyNameIndex.Contains(propertyNameHashCode))
                                    {
                                        remarked = true;
                                    }
                                    else
                                    {
                                        propertyNameIndex.Add(propertyNameHashCode);
                                    }
                                }

                                switch (attributeMetadata.AttributeType.Value)
                                {
                                    case AttributeTypeCode.BigInt:
                                        GenerateBigIntProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Boolean:
                                        GenerateBooleanProperty(service, writer, entityMetadata, attributeMetadata, ns, nsEnum, className, propertyName, remarked, isUI, useLogicalName);
                                        break;
                                    case AttributeTypeCode.DateTime:
                                        GenerateDateTimeProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Decimal:
                                        GenerateDecimalProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Double:
                                        GenerateDoubleProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Integer:
                                        GenerateIntegerProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Lookup:
                                    case AttributeTypeCode.Customer:
                                    case AttributeTypeCode.Owner:
                                        GenerateLookupProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false, useLogicalName);
                                        break;
                                    case AttributeTypeCode.ManagedProperty:
                                        GenerateManagedPropertyProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Money:
                                        GenerateMoneyProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Picklist:
                                    case AttributeTypeCode.State:
                                    case AttributeTypeCode.Status:
                                        GeneratePicklistProperty(service, writer, entityMetadata, attributeMetadata, ns, nsEnum, className, propertyName, remarked, isUI, useLogicalName);
                                        break;
                                    case AttributeTypeCode.String:
                                    case AttributeTypeCode.Memo:
                                        GenerateStringProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Uniqueidentifier:
                                        var isPrimaryId = attributeMetadata.LogicalName.Equals(entityMetadata.PrimaryIdAttribute);
                                        GenerateUniqueidentifierProperty(service, writer, entityMetadata, attributeMetadata, isPrimaryId, propertyName, remarked, isUI);

                                        break;
                                    case AttributeTypeCode.Virtual:
                                        GenerateVirtualProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI);
                                        break;
                                    case AttributeTypeCode.PartyList:
                                    case AttributeTypeCode.CalendarRules:
                                    case AttributeTypeCode.EntityName:
                                    default:
                                        GenerateUnsupportedProperty(writer, attributeMetadata, remarked);
                                        break;
                                }
                            }
                            else
                            {
                                GenerateUnsupportedPropertyName(writer, attributeMetadata, remarked);
                            }
                        }
                    }
                    else
                    {
                        GenerateUnsupportedProperty(writer, attributeMetadata, remarked);
                    }
                }

                #endregion

                #region Operator overload

                //if (useLogicalName)
                //{
                //	writer.WriteLine();
                //	GenerateClassOperatorOverload(writer, className, remarked, isUI);
                //}

                #endregion

                #region Class end

                writer.Indent--;
                writer.WriteLine(prefix + "}");

                #endregion
            }
            else
            {
                GenerateUnsupportedClassName(writer, entityMetadata);
            }
        }

        private static void GenerateClassOperatorOverload(IndentedTextWriter writer, string className, bool remarked, bool isUI)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            #region Microsoft.Xrm.Sdk.EntityReference

            writer.WriteLine(prefix + $"public static implicit operator Microsoft.Xrm.Sdk.EntityReference({className} nullableValue)");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "if (nullableValue == null)");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "return null;");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
            writer.WriteLine();

            if (isUI)
            {
                writer.WriteLine(prefix + "return nullableValue as dnmx.UI.Entity;");
            }
            else
            {
                writer.WriteLine(prefix + "return new Microsoft.Xrm.Sdk.EntityReference(nullableValue.LogicalName, nullableValue.Id) { Name = nullableValue.PF };");
            }

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            #endregion

            writer.WriteLine();

            #region System.Nullable<Lookup>

            writer.WriteLine(prefix + $"public static implicit operator System.Nullable<dnmx.Lookup>({className} nullableValue)");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "if (nullableValue == null)");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "return null;");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
            writer.WriteLine();
            writer.WriteLine(prefix + "return (Microsoft.Xrm.Sdk.EntityReference)nullableValue;");

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            #endregion
        }

        private static bool GenerateEnum(IOrganizationService service, IndentedTextWriter writer, string ns, string nsEnum, EntityMetadata entityMetadata, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            var enumAttributes = entityMetadata.Attributes.Where(
                am => am.AttributeType.GetValueOrDefault() == AttributeTypeCode.Boolean ||
                am.AttributeType.GetValueOrDefault() == AttributeTypeCode.Picklist ||
                am.AttributeType.GetValueOrDefault() == AttributeTypeCode.State ||
                am.AttributeType.GetValueOrDefault() == AttributeTypeCode.Status
            );

            if (enumAttributes.FirstOrDefault() is AttributeMetadata)
            {
                if (string.IsNullOrWhiteSpace(ns))
                {
                    writer.WriteLine(prefix + $"namespace {nsEnum}");
                }
                else
                {
                    writer.WriteLine(prefix + $"namespace {ns}.{nsEnum}");
                }

                writer.WriteLine(prefix + "{");
                writer.Indent++;

                List<int> propertyNameIndex = new List<int>();

                GenerateEnumClass(service, writer, true, ns, nsEnum, entityMetadata, enumAttributes, propertyNameIndex, remarked);
                writer.WriteLine();
                GenerateEnumClass(service, writer, false, ns, nsEnum, entityMetadata, enumAttributes, propertyNameIndex, remarked);

                writer.Indent--;
                writer.WriteLine(prefix + "}");

                return true;
            }

            return false;
        }

        private static void GenerateEnumClass(IOrganizationService service, IndentedTextWriter writer, bool useLogicalName, string ns, string nsEnum, EntityMetadata entityMetadata, IEnumerable<AttributeMetadata> enumAttributes, List<int> propertyNameIndex, bool remarked)
        {
            var originalRemarked = remarked;
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            if (TryGetClassName(useLogicalName, entityMetadata, out string className))
            {
                if (useLogicalName)
                {
                    GenerateClassSummary(writer, entityMetadata, remarked);
                    GenerateGeneratedCodeAttribute(writer, remarked);

                    writer.WriteLine(prefix + $"public partial class {className}");
                }
                else
                {
                    writer.WriteLine(prefix + $"public partial class {className}");
                }

                writer.WriteLine(prefix + "{");
                writer.Indent++;



                AttributeMetadataWithPropertyName[] attributeMetadataWithPropertyList = enumAttributes.Select(am =>
                {
                    TryGetPropertyName(useLogicalName, am, out string propertyName);

                    return new AttributeMetadataWithPropertyName
                    {
                        PropertyName = propertyName,
                        Value = am
                    };
                })
                    .OrderBy(am => am, new ClassPropertyNameComparer())
                    .ToArray();



                bool first = true;

                foreach (var attributeMetadataWithPropertyName in attributeMetadataWithPropertyList)
                {
                    remarked = originalRemarked;

                    var attributeMetadata = attributeMetadataWithPropertyName.Value;
                    var enumFieldName = attributeMetadataWithPropertyName.PropertyName;
                    var propertyNameHashCode = enumFieldName.GetHashCode();

                    if (propertyNameIndex.Contains(propertyNameHashCode))
                    {
                        remarked = true;
                    }
                    else
                    {
                        propertyNameIndex.Add(propertyNameHashCode);
                    }

                    if (!first)
                    {
                        writer.WriteLine();
                    }

                    if (useLogicalName ||
                        !string.IsNullOrWhiteSpace(enumFieldName))
                    {
                        if (enumFieldName.Equals(className))
                        {
                            remarked = true;
                        }

                        if (attributeMetadata.AttributeType.Value == AttributeTypeCode.Boolean)
                        {
                            GenerateBooleanEnum(service, writer, entityMetadata, attributeMetadata as BooleanAttributeMetadata, enumFieldName, remarked);
                        }
                        else
                        {
                            GenerateOptionSetValueEnum(service, writer, entityMetadata, attributeMetadata as EnumAttributeMetadata, enumFieldName, remarked);
                        }
                    }
                    else
                    {
                        GenerateUnsupportedEnumFieldName(writer, attributeMetadata, remarked);
                    }

                    first = false;
                }

                writer.Indent--;
                writer.WriteLine(prefix + "}");
            }
            else
            {
                GenerateUnsupportedEnumClassName(writer, entityMetadata, remarked);
            }
        }

        private static void GenerateUIClass(IOrganizationService service, IndentedTextWriter writer, bool useLogicalName, string ns, string nsEnum, string nsUI, string nsForm, List<int> propertyNameIndex, EntityMetadata entityMetadata, bool remarked)
        {
            var isUI = true;
            var originalRemarked = remarked;
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            if (TryGetClassName(useLogicalName, entityMetadata, out string className))
            {
                if (useLogicalName)
                {
                    GenerateClassSummary(writer, entityMetadata, remarked);
                    GenerateGeneratedCodeAttribute(writer, remarked);
                }

                GenerateUIClassDeclaration(writer, useLogicalName, className, remarked);

                #region Class start

                writer.WriteLine(prefix + "{");
                writer.Indent++;

                #endregion

                #region Attributes

                if (useLogicalName)
                {
                    propertyNameIndex.Add(className.GetHashCode());

                    #region Id

                    GenerateUIIdOverride(writer, entityMetadata.PrimaryIdAttribute, CLASS_ID_OVERRIDE_PROPERTYNAME, remarked);
                    propertyNameIndex.Add(CLASS_ID_OVERRIDE_PROPERTYNAME.GetHashCode());

                    #endregion

                    #region ID

                    var primaryIdAttributeMetadata = entityMetadata.Attributes.Where(am => am.LogicalName == entityMetadata.PrimaryIdAttribute).First();

                    writer.WriteLine();
                    GenerateUIIdProperty(service, writer, entityMetadata, primaryIdAttributeMetadata, CLASS_ID_PROPERTYNAME, remarked);
                    propertyNameIndex.Add(CLASS_ID_PROPERTYNAME.GetHashCode());

                    #endregion

                    #region PF

                    var primaryNameAttributeMetadata = entityMetadata.Attributes.Where(am => am.LogicalName == entityMetadata.PrimaryNameAttribute).First();

                    writer.WriteLine();
                    GenerateUIPFProperty(service, writer, entityMetadata, primaryNameAttributeMetadata, ns, nsEnum, className, CLASS_PF_PROPERTYNAME, remarked);
                    propertyNameIndex.Add(CLASS_PF_PROPERTYNAME.GetHashCode());

                    #endregion

                    #region Form

                    writer.WriteLine();
                    GenerateUIFormProperty(writer, ns, nsUI, nsForm, className, CLASS_FORM_PROPERTYNAME, remarked);
                    propertyNameIndex.Add(CLASS_FORM_PROPERTYNAME.GetHashCode());

                    #endregion

                    writer.WriteLine();
                    GenerateUIClassConstructor(writer, entityMetadata, ns, nsUI, nsForm, className, remarked);

                    var baseType = typeof(UI.AbstractEntity);
                    var bindingFlags = BindingFlags.FlattenHierarchy | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                    foreach (var memberInfo in baseType.GetProperties(bindingFlags))
                    {
                        if (memberInfo.GetIndexParameters().Length > 0)
                        {
                            continue;
                        }

                        if (!IsValidProperty(memberInfo.GetMethod) &&
                            !IsValidProperty(memberInfo.SetMethod))
                        {
                            continue;
                        }

                        var memberInfoNameHashCode = memberInfo.Name.GetHashCode();

                        if (!propertyNameIndex.Contains(memberInfoNameHashCode))
                        {
                            propertyNameIndex.Add(memberInfoNameHashCode);
                        }
                    }

                    foreach (var memberInfo in baseType.GetFields(bindingFlags))
                    {
                        if (!IsValidField(memberInfo))
                        {
                            continue;
                        }

                        var memberInfoNameHashCode = memberInfo.Name.GetHashCode();

                        if (!propertyNameIndex.Contains(memberInfoNameHashCode))
                        {
                            propertyNameIndex.Add(memberInfoNameHashCode);
                        }
                    }

                    foreach (var memberInfo in baseType.GetMethods(bindingFlags))
                    {
                        if (!IsValidMethod(memberInfo))
                        {
                            continue;
                        }

                        var memberInfoNameHashCode = memberInfo.Name.GetHashCode();

                        if (!propertyNameIndex.Contains(memberInfoNameHashCode))
                        {
                            propertyNameIndex.Add(memberInfoNameHashCode);
                        }
                    }
                }

                AttributeMetadataWithPropertyName[] attributeMetadataWithPropertyList = entityMetadata.Attributes.Select(am =>
                {
                    TryGetPropertyName(useLogicalName, am, out string propertyName);

                    return new AttributeMetadataWithPropertyName
                    {
                        PropertyName = propertyName,
                        Value = am
                    };
                })
                    .OrderBy(am => am, new ClassPropertyNameComparer())
                    .ToArray();

                for (int i = 0; i < attributeMetadataWithPropertyList.Length; i++)
                {
                    remarked = originalRemarked;

                    if (useLogicalName ||
                        i > 0)
                    {
                        writer.WriteLine();
                    }

                    var attributeMetadataWithProperty = attributeMetadataWithPropertyList[i];
                    var propertyName = attributeMetadataWithProperty.PropertyName;
                    var attributeMetadata = attributeMetadataWithProperty.Value;

                    if (attributeMetadata.AttributeType.HasValue)
                    {
                        if (attributeMetadata.AttributeType.Value == AttributeTypeCode.PartyList ||
                            attributeMetadata.AttributeType.Value == AttributeTypeCode.CalendarRules ||
                            attributeMetadata.AttributeType.Value == AttributeTypeCode.Virtual ||
                            attributeMetadata.AttributeType.Value == AttributeTypeCode.EntityName)
                        {
                            GenerateUnsupportedProperty(writer, attributeMetadata, remarked);
                        }
                        else
                        {
                            if (useLogicalName ||
                                !string.IsNullOrWhiteSpace(propertyName))
                            {
                                if (attributeMetadata.AttributeType.Value == AttributeTypeCode.BigInt ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Boolean ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.DateTime ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Decimal ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Double ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Integer ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Lookup ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Customer ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Owner ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.ManagedProperty ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Money ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Picklist ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.State ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Status ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.String ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Memo ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Uniqueidentifier ||
                                    attributeMetadata.AttributeType.Value == AttributeTypeCode.Virtual)
                                {
                                    var propertyNameHashCode = propertyName.GetHashCode();

                                    if (propertyNameIndex.Contains(propertyNameHashCode))
                                    {
                                        remarked = true;
                                    }
                                    else
                                    {
                                        propertyNameIndex.Add(propertyNameHashCode);
                                    }
                                }

                                switch (attributeMetadata.AttributeType.Value)
                                {
                                    case AttributeTypeCode.BigInt:
                                        GenerateBigIntProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Boolean:
                                        GenerateBooleanProperty(service, writer, entityMetadata, attributeMetadata, ns, nsEnum, className, propertyName, remarked, isUI, useLogicalName);
                                        break;
                                    case AttributeTypeCode.DateTime:
                                        GenerateDateTimeProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Decimal:
                                        GenerateDecimalProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Double:
                                        GenerateDoubleProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Integer:
                                        GenerateIntegerProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Lookup:
                                    case AttributeTypeCode.Customer:
                                    case AttributeTypeCode.Owner:
                                        GenerateLookupProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false, useLogicalName);
                                        break;
                                    case AttributeTypeCode.ManagedProperty:
                                        GenerateManagedPropertyProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Money:
                                        GenerateMoneyProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Picklist:
                                    case AttributeTypeCode.State:
                                    case AttributeTypeCode.Status:
                                        GeneratePicklistProperty(service, writer, entityMetadata, attributeMetadata, ns, nsEnum, className, propertyName, remarked, isUI, useLogicalName);
                                        break;
                                    case AttributeTypeCode.String:
                                    case AttributeTypeCode.Memo:
                                        GenerateStringProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, false);
                                        break;
                                    case AttributeTypeCode.Uniqueidentifier:
                                        var isPrimaryId = attributeMetadata.LogicalName.Equals(entityMetadata.PrimaryIdAttribute);
                                        GenerateUniqueidentifierProperty(service, writer, entityMetadata, attributeMetadata, isPrimaryId, propertyName, remarked, isUI);

                                        break;
                                    case AttributeTypeCode.Virtual:
                                        GenerateVirtualProperty(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI);
                                        break;
                                    case AttributeTypeCode.PartyList:
                                    case AttributeTypeCode.CalendarRules:
                                    case AttributeTypeCode.EntityName:
                                    default:
                                        GenerateUnsupportedProperty(writer, attributeMetadata, remarked);
                                        break;
                                }
                            }
                            else
                            {
                                GenerateUnsupportedPropertyName(writer, attributeMetadata, remarked);
                            }
                        }
                    }
                    else
                    {
                        GenerateUnsupportedProperty(writer, attributeMetadata, remarked);
                    }
                }

                #endregion

                #region Operator overload

                //if (useLogicalName)
                //{
                //	writer.WriteLine();
                //	GenerateClassOperatorOverload(writer, className, remarked, isUI);
                //}

                #endregion

                #region Class end

                writer.Indent--;
                writer.WriteLine(prefix + "}");

                #endregion
            }
            else
            {
                GenerateUnsupportedClassName(writer, entityMetadata);
            }
        }

        private static void GenerateUIFormClass(IOrganizationService service, IndentedTextWriter writer, bool useLogicalName, string ns, string nsEnum, string nsUI, string nsForm, List<int> propertyNameIndex, EntityMetadata entityMetadata, bool remarked)
        {
            var originalRemarked = remarked;
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            string fullNS = $"{nsUI}.{nsForm}";

            if (!string.IsNullOrWhiteSpace(ns))
            {
                fullNS = $"{ns}.{fullNS}";
            }

            if (TryGetClassName(useLogicalName, entityMetadata, out string className))
            {
                if (useLogicalName)
                {
                    GenerateClassSummary(writer, entityMetadata, remarked);
                    GenerateGeneratedCodeAttribute(writer, remarked);
                }

                writer.WriteLine(prefix + $"public partial class {className}");

                #region Class start

                writer.WriteLine(prefix + "{");
                writer.Indent++;

                #endregion

                if (useLogicalName)
                {
                    propertyNameIndex.Add(className.GetHashCode());

                    writer.WriteLine(prefix + $"protected OpenQA.Selenium.IWebDriver driver;");
                    writer.WriteLine();

                    writer.WriteLine(prefix + $"public {className}(OpenQA.Selenium.IWebDriver driver)");
                    writer.WriteLine(prefix + "{");

                    writer.Indent++;
                    writer.WriteLine(prefix + $"if (driver == null)");
                    writer.WriteLine(prefix + "{");

                    writer.Indent++;
                    writer.WriteLine(prefix + "throw new System.ArgumentNullException(nameof(driver));");

                    writer.Indent--;
                    writer.WriteLine(prefix + "}");
                    writer.WriteLine();
                    writer.WriteLine(prefix + "this.driver = driver;");

                    writer.WriteLine();
                    writer.WriteLine(prefix + "InitializeAttribute1();");
                    writer.WriteLine(prefix + "InitializeAttribute2();");

                    writer.Indent--;
                    writer.WriteLine(prefix + "}");
                }

                #region Attributes

                AttributeMetadataWithPropertyName[] attributeMetadataWithPropertyList = entityMetadata.Attributes.Select(am =>
                {
                    TryGetPropertyName(useLogicalName, am, out string propertyName);

                    return new AttributeMetadataWithPropertyName
                    {
                        PropertyName = propertyName,
                        Value = am
                    };
                })
                    .OrderBy(am => am, new ClassPropertyNameComparer())
                    .ToArray();

                List<Tuple<string, string, bool>> properties = new List<Tuple<string, string, bool>>();

                for (int i = 0; i < attributeMetadataWithPropertyList.Length; i++)
                {
                    remarked = originalRemarked;

                    if (useLogicalName ||
                        i > 0)
                    {
                        writer.WriteLine();
                    }

                    var attributeMetadataWithProperty = attributeMetadataWithPropertyList[i];
                    var propertyName = attributeMetadataWithProperty.PropertyName;
                    var attributeMetadata = attributeMetadataWithProperty.Value;

                    if (attributeMetadata.AttributeType.HasValue)
                    {
                        if (useLogicalName ||
                            !string.IsNullOrWhiteSpace(propertyName))
                        {
                            var propertyNameHashCode = propertyName.GetHashCode();

                            if (propertyNameIndex.Contains(propertyNameHashCode))
                            {
                                remarked = true;
                            }
                            else
                            {
                                propertyNameIndex.Add(propertyNameHashCode);
                            }

                            string attributePrefix = string.Empty;

                            if (remarked)
                            {
                                attributePrefix = COMMENT_PREFIX;
                            }

                            GeneratePropertySummary(service, writer, entityMetadata, attributeMetadata, remarked);

                            writer.WriteLine(attributePrefix + $"public dnmx.UI.Attribute {propertyName} {{ get; protected set; }}");

                            properties.Add(new Tuple<string, string, bool>(propertyName, attributeMetadata.LogicalName, remarked));
                        }
                        else
                        {
                            GenerateUnsupportedPropertyName(writer, attributeMetadata, remarked);
                        }
                    }
                    else
                    {
                        GenerateUnsupportedProperty(writer, attributeMetadata, remarked);
                    }
                }

                if (properties.Count > 0)
                {
                    remarked = originalRemarked;

                    string attributePrefix = string.Empty;

                    if (remarked)
                    {
                        attributePrefix = COMMENT_PREFIX;
                    }

                    writer.WriteLine();

                    if (useLogicalName)
                    {
                        writer.WriteLine(attributePrefix + "protected void InitializeAttribute1()");
                    }
                    else
                    {
                        writer.WriteLine(attributePrefix + "protected void InitializeAttribute2()");
                    }

                    writer.WriteLine(attributePrefix + "{");
                    writer.Indent++;

                    foreach (var tuple in properties)
                    {
                        if (tuple.Item3)
                        {
                            writer.WriteLine(attributePrefix + $"{COMMENT_PREFIX}{tuple.Item1} = new dnmx.UI.Attribute(driver, \"{tuple.Item2}\");");
                        }
                        else
                        {
                            writer.WriteLine(attributePrefix + $"{tuple.Item1} = new dnmx.UI.Attribute(driver, \"{tuple.Item2}\");");
                        }
                    }

                    writer.Indent--;
                    writer.WriteLine(attributePrefix + "}");
                }

                #endregion

                #region Class end

                writer.Indent--;
                writer.WriteLine(prefix + "}");

                #endregion
            }
            else
            {
                GenerateUnsupportedClassName(writer, entityMetadata);
            }
        }

        private static void GenerateBigIntProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string propertyName, bool remarked, bool isUI, bool isReadonly)
        {
            GenerateNullableProperty<WholeNumber, long>(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, isReadonly);
        }

        private static void GenerateBooleanProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string ns, string nsEnum, string className, string propertyName, bool remarked, bool isUI, bool useLogicalName)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            if (!string.IsNullOrWhiteSpace(ns))
            {
                nsEnum = $"{ns}.{nsEnum}";
            }

            GeneratePropertySummary(service, writer, entityMetadata, attributeMetadata, remarked);

            if (!isUI)
            {
                GeneratePropertyAttributes(writer, attributeMetadata, remarked);

                if (useLogicalName)
                {
                    writer.WriteLine(prefix + "[dnmx.TwoOptionsAttribute()]");
                }
            }

            writer.WriteLine(prefix + $"public System.Nullable<{nsEnum}.{className}.{propertyName}> {propertyName}");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "get");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"System.Nullable<bool> value = this.GetAttributeValue<System.Nullable<bool>>(\"{attributeMetadata.LogicalName}\");");
            writer.WriteLine();
            writer.WriteLine(prefix + "if (value.HasValue)");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "if (value.Value)");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"return ({nsEnum}.{className}.{propertyName})1;");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
            writer.WriteLine();
            writer.WriteLine(prefix + $"return ({nsEnum}.{className}.{propertyName})0;");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
            writer.WriteLine();
            writer.WriteLine(prefix + "return null;");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
            writer.WriteLine(prefix + "set");
            writer.WriteLine(prefix + "{");

            writer.Indent++;

            if (!isUI)
            {
                writer.WriteLine(prefix + $"this.{ONPROPERTYCHANGING_NAME}(\"{propertyName}\");");
                writer.WriteLine();
            }

            writer.WriteLine(prefix + "if (value.HasValue)");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"this.SetAttributeValue(\"{attributeMetadata.LogicalName}\", value.Value == ({nsEnum}.{className}.{propertyName})1);");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
            writer.WriteLine(prefix + "else");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"this.SetAttributeValue(\"{attributeMetadata.LogicalName}\", null);");

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            if (!isUI)
            {
                writer.WriteLine();
                writer.WriteLine(prefix + $"this.{ONPROPERTYCHANGED_NAME}(\"{propertyName}\");");
            }

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            writer.Indent--;
            writer.WriteLine(prefix + "}");








            //writer.WriteLine();

            //GeneratePropertySummary(writer, attributeMetadata);
            //GeneratePropertyAttributes(writer, attributeMetadata);

            //string propertyName = CreateValidIdentifier(attributeMetadata.DisplayName?.UserLocalizedLabel?.Label ?? attributeMetadata.LogicalName);

            //writer.WriteLine($"public System.Nullable<bool> {propertyName}");
            //writer.WriteLine("{");

            //writer.Indent++;
            //writer.WriteLine("get");
            //writer.WriteLine("{");

            //writer.Indent++;
            //writer.WriteLine($"return this.GetAttributeValue<System.Nullable<bool>>(\"{attributeMetadata.LogicalName}\");");

            //writer.Indent--;
            //writer.WriteLine("}");
            //writer.WriteLine("set");
            //writer.WriteLine("{");

            //writer.Indent++;
            //writer.WriteLine($"this.{ONPROPERTYCHANGING_NAME}(\"{propertyName}\");");
            //writer.WriteLine($"this.SetAttributeValue(\"{attributeMetadata.LogicalName}\", value);");
            //writer.WriteLine($"this.{ONPROPERTYCHANGED_NAME}(\"{propertyName}\");");

            //writer.Indent--;
            //writer.WriteLine("}");

            //writer.Indent--;
            //writer.WriteLine("}");



        }

        private static void GenerateDateTimeProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string propertyName, bool remarked, bool isUI, bool isReadonly)
        {
            GenerateNullableProperty<DateTime, DateTime>(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, isReadonly);



            //writer.WriteLine();

            //GeneratePropertySummary(writer, attributeMetadata);
            //GeneratePropertyAttributes(writer, attributeMetadata);

            //string propertyName = CreateValidIdentifier(attributeMetadata.DisplayName?.UserLocalizedLabel?.Label ?? attributeMetadata.LogicalName);

            //writer.WriteLine($"public System.Nullable<System.DateTime> {propertyName}");
            //writer.WriteLine("{");

            //writer.Indent++;
            //writer.WriteLine("get");
            //writer.WriteLine("{");

            //writer.Indent++;
            //writer.WriteLine($"return this.GetAttributeValue<System.Nullable<System.DateTime>>(\"{attributeMetadata.LogicalName}\");");

            //writer.Indent--;
            //writer.WriteLine("}");
            //writer.WriteLine("set");
            //writer.WriteLine("{");

            //writer.Indent++;
            //writer.WriteLine($"this.{ONPROPERTYCHANGING_NAME}(\"{propertyName}\");");
            //writer.WriteLine($"this.SetAttributeValue(\"{attributeMetadata.LogicalName}\", value);");
            //writer.WriteLine($"this.{ONPROPERTYCHANGED_NAME}(\"{propertyName}\");");

            //writer.Indent--;
            //writer.WriteLine("}");

            //writer.Indent--;
            //writer.WriteLine("}");
        }

        private static void GenerateDecimalProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string propertyName, bool remarked, bool isUI, bool isReadonly)
        {
            GenerateNullableProperty<DecimalNumber, decimal>(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, isReadonly);

            //GenerateNumericProperty<DecimalNumber, decimal>(writer, attributeMetadata);
        }

        private static void GenerateDoubleProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string propertyName, bool remarked, bool isUI, bool isReadonly)
        {
            GenerateNullableProperty<FloatingPointNumber, double>(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, isReadonly);

            //GenerateNumericProperty<FloatingPointNumber, double>(writer, attributeMetadata);
        }

        private static void GenerateIntegerProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string propertyName, bool remarked, bool isUI, bool isReadonly)
        {
            GenerateNullableProperty<WholeNumber, int>(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, isUI, isReadonly);

            //GenerateNumericProperty<WholeNumber, int>(writer, attributeMetadata);
        }

        private static void GenerateLookupProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string propertyName, bool remarked, bool isUI, bool isReadonly, bool useLogicalName)
        {
            GenerateProperty<Lookup, EntityReference>(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, true, false, isUI, isReadonly, useLogicalName: useLogicalName);

            //writer.WriteLine();

            //GeneratePropertySummary(writer, attributeMetadata);
            //GeneratePropertyAttributes(writer, attributeMetadata);

            //string propertyName = CreateValidIdentifier(attributeMetadata.DisplayName?.UserLocalizedLabel?.Label ?? attributeMetadata.LogicalName);

            //writer.WriteLine($"public System.Nullable<dnmx.Lookup> {propertyName}");
            //writer.WriteLine("{");

            //writer.Indent++;
            //writer.WriteLine("get");
            //writer.WriteLine("{");

            //writer.Indent++;
            //writer.WriteLine($"return this.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>(\"{attributeMetadata.LogicalName}\");");

            //writer.Indent--;
            //writer.WriteLine("}");
            //writer.WriteLine("set");
            //writer.WriteLine("{");

            //writer.Indent++;
            //writer.WriteLine($"this.{ONPROPERTYCHANGING_NAME}(\"{propertyName}\");");
            //writer.WriteLine($"this.SetAttributeValue(\"{attributeMetadata.LogicalName}\", (Microsoft.Xrm.Sdk.EntityReference)value);");
            //writer.WriteLine($"this.{ONPROPERTYCHANGED_NAME}(\"{propertyName}\");");

            //writer.Indent--;
            //writer.WriteLine("}");

            //writer.Indent--;
            //writer.WriteLine("}");
        }

        private static void GenerateManagedPropertyProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string propertyName, bool remarked, bool isUI, bool isReadonly)
        {
            GenerateProperty<BooleanManagedProperty, BooleanManagedProperty>(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, false, false, isUI, isReadonly);


            //writer.WriteLine();

            //GeneratePropertySummary(writer, attributeMetadata);
            //GeneratePropertyAttributes(writer, attributeMetadata);

            //string propertyName = CreateValidIdentifier(attributeMetadata.DisplayName?.UserLocalizedLabel?.Label ?? attributeMetadata.LogicalName);

            //writer.WriteLine($"public Microsoft.Xrm.Sdk.BooleanManagedProperty {propertyName}");
            //writer.WriteLine("{");

            //writer.Indent++;
            //writer.WriteLine("get");
            //writer.WriteLine("{");

            //writer.Indent++;
            //writer.WriteLine($"return this.GetAttributeValue<Microsoft.Xrm.Sdk.BooleanManagedProperty>(\"{attributeMetadata.LogicalName}\");");

            //writer.Indent--;
            //writer.WriteLine("}");
            //writer.WriteLine("set");
            //writer.WriteLine("{");

            //writer.Indent++;
            //writer.WriteLine($"this.{ONPROPERTYCHANGING_NAME}(\"{propertyName}\");");
            //writer.WriteLine($"this.SetAttributeValue(\"{attributeMetadata.LogicalName}\", value);");
            //writer.WriteLine($"this.{ONPROPERTYCHANGED_NAME}(\"{propertyName}\");");

            //writer.Indent--;
            //writer.WriteLine("}");

            //writer.Indent--;
            //writer.WriteLine("}");
        }

        private static void GenerateMoneyProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string propertyName, bool remarked, bool isUI, bool isReadonly)
        {
            GenerateProperty<Currency, Money>(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, true, false, isUI, isReadonly);
            //GenerateNumericProperty(writer, attributeMetadata, $"System.Nullable<{typeof(Currency).FullName}>", "Microsoft.Xrm.Sdk.Money");
        }

        private static void GeneratePicklistProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string ns, string nsEnum, string className, string propertyName, bool remarked, bool isUI, bool useLogicalName)
        {
            //GenerateProperty<dnmx.OptionSetValue, Microsoft.Xrm.Sdk.OptionSetValue>(writer, attributeMetadata, propertyName, true);

            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            if (!string.IsNullOrWhiteSpace(ns))
            {
                nsEnum = $"{ns}.{nsEnum}";
            }

            GeneratePropertySummary(service, writer, entityMetadata, attributeMetadata, remarked);

            if (!isUI)
            {
                GeneratePropertyAttributes(writer, attributeMetadata, remarked);

                if (useLogicalName)
                {
                    writer.WriteLine(prefix + "[dnmx.OptionSetAttribute()]");
                }
            }

            writer.WriteLine(prefix + $"public System.Nullable<{nsEnum}.{className}.{propertyName}> {propertyName}");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "get");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"Microsoft.Xrm.Sdk.OptionSetValue value = this.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>(\"{attributeMetadata.LogicalName}\");");
            writer.WriteLine();
            writer.WriteLine(prefix + "if (value == null)");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "return null;");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
            writer.WriteLine();
            writer.WriteLine(prefix + $"return ({nsEnum}.{className}.{propertyName})value.Value;");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
            writer.WriteLine(prefix + "set");
            writer.WriteLine(prefix + "{");

            writer.Indent++;

            if (!isUI)
            {
                writer.WriteLine(prefix + $"this.{ONPROPERTYCHANGING_NAME}(\"{propertyName}\");");
                writer.WriteLine();
            }

            writer.WriteLine(prefix + "if (value.HasValue)");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"this.SetAttributeValue(\"{attributeMetadata.LogicalName}\", new Microsoft.Xrm.Sdk.OptionSetValue((int)value.Value));");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
            writer.WriteLine(prefix + "else");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"this.SetAttributeValue(\"{attributeMetadata.LogicalName}\", null);");

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            if (!isUI)
            {
                writer.WriteLine();
                writer.WriteLine(prefix + $"this.{ONPROPERTYCHANGED_NAME}(\"{propertyName}\");");
            }

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
        }

        private static void GenerateStringProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string propertyName, bool remarked, bool isUI, bool isReadonly, bool isOverride = false)
        {
            GenerateProperty<string, string>(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, false, false, isUI, isReadonly, isOverride);
        }

        private static void GenerateUIPFProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string ns, string nsEnum, string className, string propertyName, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            GeneratePropertySummary(service, writer, entityMetadata, attributeMetadata, remarked);

            writer.WriteLine(prefix + $"public override System.String {propertyName}");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "get");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"return base.{propertyName};");
            //writer.WriteLine(prefix + "return this.GetName();");

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
        }

        private static void GenerateIdOverride(IndentedTextWriter writer, string primaryIdAttributeLogicalName, string propertyName, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            writer.WriteLine(prefix + "/// <summary>");
            writer.WriteLine(prefix + "/// Gets or sets the ID of the record represented by this entity instance.");
            writer.WriteLine(prefix + "/// </summary>");
            writer.WriteLine(prefix + $"[Microsoft.Xrm.Sdk.AttributeLogicalNameAttribute(\"{primaryIdAttributeLogicalName}\")]");

            writer.WriteLine(prefix + $"public override System.Guid {propertyName}");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "get");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"return base.Id;");

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            writer.WriteLine(prefix + "set");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "if (value == System.Guid.Empty)");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"this.{primaryIdAttributeLogicalName} = null;");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
            writer.WriteLine(prefix + "else");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"this.{primaryIdAttributeLogicalName} = value;");

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
        }

        private static void GenerateIdProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string propertyName, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            GeneratePropertySummary(service, writer, entityMetadata, attributeMetadata, remarked);

            //writer.WriteLine(prefix + "/// <summary>");
            //writer.WriteLine(prefix + "/// Gets or sets the ID of the record represented by this entity instance.");
            //writer.WriteLine(prefix + "/// </summary>");
            writer.WriteLine(prefix + $"[Microsoft.Xrm.Sdk.AttributeLogicalNameAttribute(\"{attributeMetadata.LogicalName}\")]");

            writer.WriteLine(prefix + $"public System.Nullable<System.Guid> {propertyName}");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "get");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"return this.GetAttributeValue<System.Nullable<System.Guid>>(\"{attributeMetadata.LogicalName}\");");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
            //writer.WriteLine(prefix + "set");
            //writer.WriteLine(prefix + "{");

            //writer.Indent++;
            //writer.WriteLine(prefix + $"this.{ONPROPERTYCHANGING_NAME}(\"{propertyName}\");");
            //writer.WriteLine(prefix + $"this.SetAttributeValue(\"{attributeMetadata.LogicalName}\", value);");

            //writer.WriteLine();
            //writer.WriteLine(prefix + "if (value.HasValue)");
            //writer.WriteLine(prefix + "{");

            //writer.Indent++;
            //writer.WriteLine(prefix + "base.Id = value.Value;");

            //writer.Indent--;
            //writer.WriteLine(prefix + "}");
            //writer.WriteLine(prefix + "else");
            //writer.WriteLine(prefix + "{");

            //writer.Indent++;
            //writer.WriteLine(prefix + "base.Id = System.Guid.Empty;");

            //writer.Indent--;
            //writer.WriteLine(prefix + "}");
            //writer.WriteLine();
            //writer.WriteLine(prefix + $"this.{ONPROPERTYCHANGED_NAME}(\"{propertyName}\");");

            //writer.Indent--;
            //writer.WriteLine(prefix + "}");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
        }

        private static void GenerateUIIdOverride(IndentedTextWriter writer, string primaryIdAttributeLogicalName, string propertyName, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            writer.WriteLine(prefix + "/// <summary>");
            writer.WriteLine(prefix + "/// Gets the ID of the record represented by this entity instance.");
            writer.WriteLine(prefix + "/// </summary>");
            //writer.WriteLine(prefix + $"[Microsoft.Xrm.Sdk.AttributeLogicalNameAttribute(\"{primaryIdAttributeLogicalName}\")]");

            writer.WriteLine(prefix + $"public override System.Guid {propertyName}");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "get");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"return base.Id;");

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
        }

        private static void GenerateUIIdProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string propertyName, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            GeneratePropertySummary(service, writer, entityMetadata, attributeMetadata, remarked);

            writer.WriteLine(prefix + $"public override System.Nullable<System.Guid> {propertyName}");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "get");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"return base.{propertyName};");

            //writer.WriteLine(prefix + "System.Guid id = base.Id;");
            //writer.WriteLine();
            //writer.WriteLine(prefix + "if (id == System.Guid.Empty)");
            //writer.WriteLine(prefix + "{");

            //writer.Indent++;
            //writer.WriteLine(prefix + "return null;");

            //writer.Indent--;
            //writer.WriteLine(prefix + "}");
            //writer.WriteLine();
            //writer.WriteLine(prefix + "return id;");

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
        }

        private static void GenerateUniqueidentifierProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, bool isPrimaryId, string propertyName, bool remarked, bool isUI)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            GeneratePropertySummary(service, writer, entityMetadata, attributeMetadata, remarked);

            if (!isUI)
            {
                GeneratePropertyAttributes(writer, attributeMetadata, remarked);
            }

            writer.WriteLine(prefix + $"public System.Nullable<System.Guid> {propertyName}");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "get");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"return this.GetAttributeValue<System.Nullable<System.Guid>>(\"{attributeMetadata.LogicalName}\");");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
            writer.WriteLine(prefix + "set");
            writer.WriteLine(prefix + "{");

            writer.Indent++;

            if (!isUI)
            {
                writer.WriteLine(prefix + $"this.{ONPROPERTYCHANGING_NAME}(\"{propertyName}\");");
            }

            writer.WriteLine(prefix + $"this.SetAttributeValue(\"{attributeMetadata.LogicalName}\", value);");

            if (isPrimaryId)
            {
                writer.WriteLine();
                writer.WriteLine(prefix + "if (value.HasValue)");
                writer.WriteLine(prefix + "{");

                writer.Indent++;
                writer.WriteLine(prefix + "base.Id = value.Value;");

                writer.Indent--;
                writer.WriteLine(prefix + "}");
                writer.WriteLine(prefix + "else");
                writer.WriteLine(prefix + "{");

                writer.Indent++;
                writer.WriteLine(prefix + "base.Id = System.Guid.Empty;");

                writer.Indent--;
                writer.WriteLine(prefix + "}");
                writer.WriteLine();
            }

            if (!isUI)
            {
                writer.WriteLine(prefix + $"this.{ONPROPERTYCHANGED_NAME}(\"{propertyName}\");");
            }

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
        }

        private static void GenerateVirtualProperty(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string propertyName, bool remarked, bool isUI)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            GeneratePropertySummary(service, writer, entityMetadata, attributeMetadata, remarked);

            if (!isUI)
            {
                GeneratePropertyAttributes(writer, attributeMetadata, remarked);
            }

            writer.WriteLine(prefix + $"public System.String {propertyName}");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "get");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"return this.GetAttributeValue<System.String>(\"{attributeMetadata.LogicalName}\");");

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
        }

        private static void GenerateUnsupportedProperty(IndentedTextWriter writer, AttributeMetadata attributeMetadata, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            string attributeName = attributeMetadata.LogicalName;

            if (!string.IsNullOrWhiteSpace(attributeMetadata.DisplayName?.UserLocalizedLabel?.Label))
            {
                attributeName = $"{attributeMetadata.DisplayName?.UserLocalizedLabel?.Label} ({attributeName})";
            }

            if (attributeMetadata.AttributeType.HasValue)
            {
                writer.WriteLine(prefix + $"// Unable to generate property for: {attributeName}. AttributeType: \"{System.Enum.GetName(typeof(AttributeTypeCode), attributeMetadata.AttributeType.Value)}\" is not supported.");
            }
            else
            {
                writer.WriteLine(prefix + $"// Unable to generate property for: {attributeName}. AttributeType is not specified.");
            }
        }

        private static void GenerateClassSummary(IndentedTextWriter writer, EntityMetadata entityMetadata, bool remarked, bool isCollection = false)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            writer.WriteLine(prefix + "/// <summary>");

            if (isCollection)
            {
                if (string.IsNullOrWhiteSpace(entityMetadata.DisplayCollectionName?.UserLocalizedLabel?.Label))
                {
                    writer.WriteLine(prefix + $"/// {entityMetadata.LogicalName}");
                }
                else
                {
                    writer.WriteLine(prefix + $"/// {EscapeXmlText(entityMetadata.DisplayCollectionName?.UserLocalizedLabel?.Label)} ({entityMetadata.LogicalName})");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(entityMetadata.DisplayName?.UserLocalizedLabel?.Label))
                {
                    writer.WriteLine(prefix + $"/// {entityMetadata.LogicalName}");
                }
                else
                {
                    writer.WriteLine(prefix + $"/// {EscapeXmlText(entityMetadata.DisplayName?.UserLocalizedLabel?.Label)} ({entityMetadata.LogicalName})");
                }
            }

            if (!string.IsNullOrWhiteSpace(entityMetadata.Description?.UserLocalizedLabel?.Label))
            {
                writer.WriteLine(prefix + $"/// <para>{EscapeXmlText(entityMetadata.Description?.UserLocalizedLabel?.Label)}</para>");
            }

            writer.WriteLine(prefix + "/// </summary>");
        }

        private static void GenerateClassConstructorSummary(IndentedTextWriter writer, string ns, string className, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            writer.WriteLine(prefix + "/// <summary>");

            if (string.IsNullOrWhiteSpace(ns))
            {
                writer.WriteLine(prefix + $"/// Initializes a new instance of the <c>{className}</c> class.");
            }
            else
            {
                writer.WriteLine(prefix + $"/// Initializes a new instance of the <c>{ns}.{className}</c> class.");
            }

            writer.WriteLine(prefix + "/// </summary>");
        }

        private static void GenerateClassConstructorSummary2(IndentedTextWriter writer, string ns, string className, string constructorParameterName, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            writer.WriteLine(prefix + "/// <summary>");

            if (string.IsNullOrWhiteSpace(ns))
            {
                writer.WriteLine(prefix + $"/// Initializes a new instance of the <c>{className}</c> class using the specified record ID.");
            }
            else
            {
                writer.WriteLine(prefix + $"/// Initializes a new instance of the <c>{ns}.{className}</c> class using the specified record ID.");
            }

            writer.WriteLine(prefix + "/// </summary>");
            writer.WriteLine(prefix + $"/// <param name=\"{constructorParameterName}\">Specifies the ID of the record.</param>");
        }

        private static void GenerateUIClassConstructorSummary(IndentedTextWriter writer, string ns, string nsUI, string className, string constructorParameterName, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            writer.WriteLine(prefix + "/// <summary>");

            if (string.IsNullOrWhiteSpace(ns))
            {
                writer.WriteLine(prefix + $"/// Initializes a new instance of the <c>{nsUI}.{className}</c> class.");
            }
            else
            {
                writer.WriteLine(prefix + $"/// Initializes a new instance of the <c>{ns}.{nsUI}.{className}</c> class.");
            }

            writer.WriteLine(prefix + "/// </summary>");
            writer.WriteLine(prefix + $"/// <param name=\"{constructorParameterName}\"><c>XrmApp</c> instance for the currently active <c>IWebDriver</c> session.</param>");
        }

        private static void GenerateClassAttributes(IndentedTextWriter writer, bool useLogicalName, EntityMetadata entityMetadata, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            if (useLogicalName)
            {
                writer.WriteLine(prefix + "[System.Runtime.Serialization.DataContractAttribute()]");
                writer.WriteLine(prefix + $"[Microsoft.Xrm.Sdk.Client.EntityLogicalNameAttribute(\"{entityMetadata.LogicalName}\")]");

                if (entityMetadata.ObjectTypeCode.HasValue)
                {
                    writer.WriteLine(prefix + $"[dnmx.EntityTypeCodeAttribute({entityMetadata.ObjectTypeCode.Value})]");
                }
            }

            GenerateGeneratedCodeAttribute(writer, remarked);
        }

        private static void GenerateGeneratedCodeAttribute(IndentedTextWriter writer, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            System.Reflection.AssemblyName assemblyName = System.Reflection.Assembly.GetAssembly(typeof(Program)).GetName();
            writer.WriteLine(prefix + $"[System.CodeDom.Compiler.GeneratedCodeAttribute(\"{assemblyName.Name}\", \"{assemblyName.Version}\")]");
        }

        private static void GenerateClassDeclaration(IndentedTextWriter writer, bool useLogicalName, string className, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            if (useLogicalName)
            {
                writer.WriteLine(prefix + $"public partial class {className} : dnmx.AbstractEntity");
            }
            else
            {
                writer.WriteLine(prefix + $"public partial class {className}");
            }
        }

        private static void GenerateUIClassDeclaration(IndentedTextWriter writer, bool useLogicalName, string className, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            if (useLogicalName)
            {
                writer.WriteLine(prefix + $"public partial class {className} : dnmx.UI.AbstractEntity");
            }
            else
            {
                writer.WriteLine(prefix + $"public partial class {className}");
            }
        }

        private static void GenerateClassConstructor(IndentedTextWriter writer, string ns, EntityMetadata entityMetadata, string className, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            GenerateClassConstructorSummary(writer, ns, className, remarked);
            writer.WriteLine(prefix + $"public {className}() : base(\"{entityMetadata.LogicalName}\") " + "{ }");

            writer.WriteLine();
            string constructorIdParameterName = CLASS_CONSTRUCTOR_ID_PARAMETERNAME;
            GenerateClassConstructorSummary2(writer, ns, className, constructorIdParameterName, remarked);
            writer.WriteLine(prefix + $"public {className}(System.Guid id) : base(\"{entityMetadata.LogicalName}\", {constructorIdParameterName}) " + "{ }");
        }

        private static void GenerateUIFormProperty(IndentedTextWriter writer, string ns, string nsUI, string nsForm, string className, string propertyName, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            string fullNS = $"{nsUI}.{nsForm}";

            if (!string.IsNullOrWhiteSpace(ns))
            {
                fullNS = $"{ns}.{fullNS}";
            }

            writer.WriteLine(prefix + $"/// <summary>");
            writer.WriteLine(prefix + $"/// Provides properties and methods to retrieve information about the user interface (UI) as well as collections for several subcomponents of the form.");
            writer.WriteLine(prefix + $"/// </summary>");
            writer.WriteLine(prefix + $"public {fullNS}.{className} {propertyName} {{ get; protected set; }}");
        }

        private static void GenerateUIClassConstructor(IndentedTextWriter writer, EntityMetadata entityMetadata, string ns, string nsUI, string nsForm, string className, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            string fullNS = $"{nsUI}.{nsForm}";

            if (!string.IsNullOrWhiteSpace(ns))
            {
                fullNS = $"{ns}.{fullNS}";
            }

            string constructorParameterName = CLASS_CONSTRUCTOR_XRMAPP_PARAMETERNAME;
            GenerateUIClassConstructorSummary(writer, ns, nsUI, className, constructorParameterName, remarked);

            writer.WriteLine(prefix + $"public {className}(Microsoft.Dynamics365.UIAutomation.Api.UCI.XrmApp {constructorParameterName}) : base({constructorParameterName}, \"{entityMetadata.LogicalName}\")");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"Form = new {fullNS}.{className}(driver);");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
        }

        private static void GeneratePropertyEventHandler(IndentedTextWriter writer, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            writer.WriteLine(prefix + $"public event System.ComponentModel.PropertyChangingEventHandler {PROPERTYCHANGING_NAME};");
            writer.WriteLine(prefix + $"public event System.ComponentModel.PropertyChangedEventHandler {PROPERTYCHANGED_NAME};");

            writer.WriteLine();
            writer.WriteLine(prefix + $"protected void {ONPROPERTYCHANGING_NAME}(string propertyName)");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"if ((this.{PROPERTYCHANGING_NAME} != null))");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"this.{PROPERTYCHANGING_NAME}(this, new System.ComponentModel.PropertyChangingEventArgs(propertyName));");

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            writer.WriteLine();
            writer.WriteLine(prefix + $"protected void {ONPROPERTYCHANGED_NAME}(string propertyName)");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"if ((this.{PROPERTYCHANGED_NAME} != null))");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"this.{PROPERTYCHANGED_NAME}(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));");

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            writer.Indent--;
            writer.WriteLine(prefix + "}");
        }

        private static void GeneratePropertySummary(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            writer.WriteLine(prefix + "/// <summary>");

            if (string.IsNullOrWhiteSpace(attributeMetadata.DisplayName?.UserLocalizedLabel?.Label))
            {
                writer.WriteLine(prefix + $"/// {attributeMetadata.LogicalName}");
            }
            else
            {
                writer.WriteLine(prefix + $"/// {EscapeXmlText(attributeMetadata.DisplayName?.UserLocalizedLabel?.Label)} ({attributeMetadata.LogicalName})");
            }

            if (entityMetadata.PrimaryIdAttribute != null &&
                attributeMetadata.LogicalName == entityMetadata.PrimaryIdAttribute)
            {
                writer.WriteLine(prefix + $"/// <para>Record ID</para>");
            }

            if (entityMetadata.PrimaryNameAttribute != null &&
                attributeMetadata.LogicalName == entityMetadata.PrimaryNameAttribute)
            {
                writer.WriteLine(prefix + $"/// <para>Primary Field</para>");
            }

            if (!string.IsNullOrWhiteSpace(attributeMetadata.Description?.UserLocalizedLabel?.Label))
            {
                writer.WriteLine(prefix + $"/// <para>{EscapeXmlText(attributeMetadata.Description?.UserLocalizedLabel?.Label)}</para>");
            }

            if (attributeMetadata is LookupAttributeMetadata lookupAttributeMetadata &&
                lookupAttributeMetadata.Targets != null &&
                lookupAttributeMetadata.Targets.Length > 0)
            {
                if (lookupAttributeMetadata.Targets.Length == 1)
                {
                    writer.Write(prefix + $"/// <para>Target entity: ");
                }
                else
                {
                    writer.Write(prefix + $"/// <para>Target entities: ");
                }

                for (int i = 0; i < lookupAttributeMetadata.Targets.Length; i++)
                {
                    var entityLogicalName = lookupAttributeMetadata.Targets[i];
                    var entityLogicalNameHash = entityLogicalName.GetHashCode();
                    EntityMetadata targetEntityMetadata;

                    if (entityMetadataCache.ContainsKey(entityLogicalNameHash))
                    {
                        targetEntityMetadata = entityMetadataCache[entityLogicalNameHash];
                    }
                    else
                    {
                        var request = new RetrieveEntityRequest()
                        {
                            LogicalName = entityLogicalName,
                            EntityFilters = EntityFilters.Entity,
                            RetrieveAsIfPublished = false,
                        };

                        var response = service.Execute(request) as RetrieveEntityResponse;
                        targetEntityMetadata = response?.EntityMetadata;

                        entityMetadataCache.Add(entityLogicalNameHash, targetEntityMetadata);
                    }

                    if (i > 0)
                    {
                        writer.Write(", ");
                    }

                    if (string.IsNullOrWhiteSpace(targetEntityMetadata.DisplayName?.UserLocalizedLabel?.Label))
                    {
                        writer.Write($"{targetEntityMetadata.LogicalName}");
                    }
                    else
                    {
                        writer.Write($"{EscapeXmlText(targetEntityMetadata.DisplayName?.UserLocalizedLabel?.Label)} ({targetEntityMetadata.LogicalName})");
                    }
                }

                writer.WriteLine("</para>");
            }

            writer.WriteLine(prefix + $"/// </summary>");
        }

        private static void GeneratePropertyAttributes(IndentedTextWriter writer, AttributeMetadata attributeMetadata, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            writer.WriteLine(prefix + $"[Microsoft.Xrm.Sdk.AttributeLogicalNameAttribute(\"{attributeMetadata.LogicalName}\")]");
        }

        private static void GenerateNullableProperty<T1, T2>(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string propertyName, bool remarked, bool isUI, bool isReadonly)
        {
            GenerateProperty<T1, T2>(service, writer, entityMetadata, attributeMetadata, propertyName, remarked, true, true, isUI, isReadonly);
        }

        private static void GenerateProperty<T1, T2>(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, AttributeMetadata attributeMetadata, string propertyName, bool remarked, bool nullableT1, bool nullableT2, bool isUI, bool isReadonly, bool isOverride = false, bool useLogicalName = false)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            string t1 = typeof(T1).FullName;
            string t2 = typeof(T2).FullName;

            if (nullableT1)
            {
                t1 = $"System.Nullable<{t1}>";
            }

            if (nullableT2)
            {
                t2 = $"System.Nullable<{t2}>";
            }

            GeneratePropertySummary(service, writer, entityMetadata, attributeMetadata, remarked);

            if (!isUI)
            {
                GeneratePropertyAttributes(writer, attributeMetadata, remarked);

                if (useLogicalName)
                {
                    if (attributeMetadata is LookupAttributeMetadata lookupAttributeMetadata)
                    {
                        writer.Write(prefix + "[dnmx.LookupAttribute(");

                        if (lookupAttributeMetadata.Format.HasValue)
                        {
                            switch (lookupAttributeMetadata.Format.Value)
                            {
                                case LookupFormat.None:
                                    writer.Write($"{typeof(LookupFormat).FullName}.{nameof(LookupFormat.None)}");
                                    break;
                                case LookupFormat.Connection:
                                    writer.Write($"{typeof(LookupFormat).FullName}.{nameof(LookupFormat.Connection)}");
                                    break;
                                case LookupFormat.Regarding:
                                    writer.Write($"{typeof(LookupFormat).FullName}.{nameof(LookupFormat.Regarding)}");
                                    break;
                                case LookupFormat.Text:
                                    writer.Write($"{typeof(LookupFormat).FullName}.{nameof(LookupFormat.Text)}");
                                    break;
                                default:
                                    break;
                            }
                        }

                        for (int i = 0; i < lookupAttributeMetadata.Targets.Length; i++)
                        {
                            if (i > 0 ||
                                lookupAttributeMetadata.Format.HasValue)
                            {
                                writer.Write(", ");
                            }

                            writer.Write($"\"{lookupAttributeMetadata.Targets[i]}\"");
                        }

                        writer.WriteLine(")]");
                    }
                }
            }

            if (isOverride)
            {
                writer.WriteLine(prefix + $"public override {t1} {propertyName}");
            }
            else
            {
                writer.WriteLine(prefix + $"public {t1} {propertyName}");
            }

            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + "get");
            writer.WriteLine(prefix + "{");

            writer.Indent++;
            writer.WriteLine(prefix + $"return this.GetAttributeValue<{t2}>(\"{attributeMetadata.LogicalName}\");");

            writer.Indent--;
            writer.WriteLine(prefix + "}");

            if (!isReadonly)
            {
                writer.WriteLine(prefix + "set");
                writer.WriteLine(prefix + "{");
                writer.Indent++;

                if (!isUI)
                {
                    writer.WriteLine(prefix + $"this.{ONPROPERTYCHANGING_NAME}(\"{propertyName}\");");
                }

                if (t1 == t2)
                {
                    writer.WriteLine(prefix + $"this.SetAttributeValue(\"{attributeMetadata.LogicalName}\", value);");
                }
                else
                {
                    writer.WriteLine(prefix + $"this.SetAttributeValue(\"{attributeMetadata.LogicalName}\", ({t2})value);");
                }

                if (!isUI)
                {
                    writer.WriteLine(prefix + $"this.{ONPROPERTYCHANGED_NAME}(\"{propertyName}\");");
                }

                writer.Indent--;
                writer.WriteLine(prefix + "}");
            }

            writer.Indent--;
            writer.WriteLine(prefix + "}");
        }

        private static void GenerateBooleanEnum(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, BooleanAttributeMetadata attributeMetadata, string enumFieldName, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            GeneratePropertySummary(service, writer, entityMetadata, attributeMetadata, remarked);
            //GenerateGeneratedCodeAttribute(writer, remarked);

            writer.WriteLine(prefix + $"public enum {enumFieldName}");
            writer.WriteLine(prefix + "{");
            writer.Indent++;

            #region False

            var falseOption = attributeMetadata.OptionSet.FalseOption;
            var falseLabel = CreateValidIdentifier(falseOption.Label.UserLocalizedLabel.Label);

            if (string.IsNullOrWhiteSpace(falseLabel))
            {
                falseLabel = "False";
            }

            var falseValue = "false";

            GenerateEnumSummary(writer, falseOption, falseValue, remarked);
            //GenerateGeneratedCodeAttribute(writer, remarked);
            writer.WriteLine(prefix + $"{falseLabel} = 0,");

            #endregion

            #region True

            var trueOption = attributeMetadata.OptionSet.TrueOption;
            var trueLabel = CreateValidIdentifier(trueOption.Label.UserLocalizedLabel.Label);

            if (string.IsNullOrWhiteSpace(trueLabel))
            {
                trueLabel = "True";
            }

            var trueValue = "true";

            if (trueLabel == falseLabel)
            {
                trueLabel = $"{trueLabel}_2";
            }

            GenerateEnumSummary(writer, trueOption, trueValue, remarked);
            //GenerateGeneratedCodeAttribute(writer, remarked);
            writer.WriteLine(prefix + $"{trueLabel} = 1");

            #endregion

            writer.Indent--;
            writer.WriteLine(prefix + "}");
        }

        private static void GenerateOptionSetValueEnum(IOrganizationService service, IndentedTextWriter writer, EntityMetadata entityMetadata, EnumAttributeMetadata attributeMetadata, string enumFieldName, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            GeneratePropertySummary(service, writer, entityMetadata, attributeMetadata, remarked);
            //GenerateGeneratedCodeAttribute(writer, remarked);

            writer.WriteLine(prefix + $"public enum {enumFieldName}");
            writer.WriteLine(prefix + "{");
            writer.Indent++;




            OptionMetadataWithLabel[] OptionMetadataWithLabelList = attributeMetadata.OptionSet.Options.Select(om =>
            {
                var label = CreateValidIdentifier(om.Label?.UserLocalizedLabel?.Label);

                return new OptionMetadataWithLabel
                {
                    Label = label,
                    Value = om
                };
            })
                .OrderBy(om => om, new OptionMetadataComparer())
                .ToArray();


            Dictionary<int, int> optionLabelIndex = new Dictionary<int, int>();

            for (int i = 0; i < OptionMetadataWithLabelList.Length; i++)
            {
                var optionMetadataWithLabel = OptionMetadataWithLabelList[i];
                var optionMetadata = optionMetadataWithLabel.Value;
                var optionLabel = optionMetadataWithLabel.Label;

                if (string.IsNullOrWhiteSpace(optionLabel))
                {
                    optionLabel = $"_Value_{optionMetadata.Value.Value}";
                }

                var optionLabelHashCode = optionLabel.GetHashCode();

                if (optionLabelIndex.ContainsKey(optionLabelHashCode))
                {
                    int duplicateCount = optionLabelIndex[optionLabelHashCode] + 1;

                    optionLabel = $"{optionLabel}_{duplicateCount}";
                    optionLabelIndex[optionLabelHashCode] = duplicateCount;
                }
                else
                {
                    optionLabelIndex.Add(optionLabelHashCode, 1);
                }

                var optionValue = optionMetadata.Value.Value.ToString();

                GenerateEnumSummary(writer, optionMetadata, optionValue, remarked);
                //GenerateGeneratedCodeAttribute(writer, remarked);
                writer.Write(prefix + $"{optionLabel} = {optionValue}");

                if (i < attributeMetadata.OptionSet.Options.Count - 1)
                {
                    writer.Write(",");
                }

                writer.WriteLine();
            }

            writer.Indent--;
            writer.WriteLine(prefix + "}");
        }

        private static void GenerateEnumSummary(IndentedTextWriter writer, OptionMetadata option, string optionValue, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            writer.WriteLine(prefix + "/// <summary>");

            if (!string.IsNullOrWhiteSpace(option.Label?.UserLocalizedLabel?.Label))
            {
                writer.WriteLine(prefix + $"/// {EscapeXmlText($"{optionValue}: \"{option.Label?.UserLocalizedLabel?.Label}\"")}");
            }
            else
            {
                writer.WriteLine(prefix + $"/// {EscapeXmlText(optionValue)}");
            }

            if (!string.IsNullOrWhiteSpace(option.Description?.UserLocalizedLabel?.Label))
            {
                writer.WriteLine(prefix + $"/// <para>{EscapeXmlText(option.Description?.UserLocalizedLabel?.Label)}</para>");
            }

            writer.WriteLine(prefix + "/// </summary>");



            //writer.WriteLine("/// <summary>");

            //if (!string.IsNullOrWhiteSpace(option.Description?.UserLocalizedLabel?.Label))
            //{
            //	writer.WriteLine($"/// <para>{EscapeXmlText(option.Description?.UserLocalizedLabel?.Label)}</para>");
            //}

            //if (!string.IsNullOrWhiteSpace(option.Label?.UserLocalizedLabel?.Label))
            //{
            //	writer.WriteLine($"/// <para><b>{EscapeXmlText($"{optionValue}: {option.Label?.UserLocalizedLabel?.Label}")}</b></para>");
            //}
            //else
            //{
            //	writer.WriteLine($"/// <para><b>{EscapeXmlText(optionValue)}</b></para>");
            //}

            //writer.WriteLine("/// </summary>");
        }

        private static string CreateValidIdentifier(string label)
        {
            if (label == null)
            {
                return null;
            }

            var normalizedLabel = label.Normalize().Replace(" ", string.Empty);
            //normalizedLabel = Regex.Replace(normalizedLabel, @"([^\d\w]+)", "_");
            normalizedLabel = Regex.Replace(normalizedLabel, @"([^\d\w]+)", string.Empty);

            if (Regex.IsMatch(normalizedLabel, @"^\d"))
            {
                normalizedLabel = $"_{normalizedLabel}";
            }

            return normalizedLabel;
        }

        private static string EscapeXmlText(string text)
        {
            var normalizedText = text.Normalize().Replace("\r", string.Empty).Replace("\n", string.Empty);

            XmlDocument doc = new XmlDocument();
            XmlNode node = doc.CreateElement("root");
            node.InnerText = normalizedText;

            return node.InnerXml;
        }

        private static bool TryGetClassName(bool useLogicalName, EntityMetadata entityMetadata, out string className)
        {
            //if (useLogicalName)
            //{
            //	className = entityMetadata.LogicalName;
            //}
            //else
            //{
            className = CreateValidIdentifier(entityMetadata.DisplayName?.UserLocalizedLabel?.Label);

            if (string.IsNullOrWhiteSpace(className))
            {
                //className = null;
                //return false;

                className = entityMetadata.LogicalName;
            }
            //}

            return true;
        }

        private static bool TryGetPropertyName(bool useLogicalName, AttributeMetadata attributeMetadata, out string propertyName)
        {
            if (useLogicalName)
            {
                propertyName = attributeMetadata.LogicalName;
            }
            else
            {
                propertyName = CreateValidIdentifier(attributeMetadata.DisplayName?.UserLocalizedLabel?.Label);

                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    propertyName = null;
                    return false;
                }
            }

            return true;
        }

        private static void GenerateUnsupportedClassName(IndentedTextWriter writer, EntityMetadata entityMetadata)
        {
            writer.WriteLine($"// Unable to generate class using display name for: {entityMetadata.LogicalName}.");
        }

        private static void GenerateUnsupportedEnumClassName(IndentedTextWriter writer, EntityMetadata entityMetadata, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            writer.WriteLine(prefix + $"// Unable to generate enum class using display name for: {entityMetadata.LogicalName}. Please refer to logical name for property access.");
        }

        private static void GenerateUnsupportedPropertyName(IndentedTextWriter writer, AttributeMetadata attributeMetadata, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            writer.WriteLine(prefix + $"// Unable to generate property using display name for: {attributeMetadata.LogicalName}. Please refer to logical name for property access.");
        }

        private static void GenerateUnsupportedEnumFieldName(IndentedTextWriter writer, AttributeMetadata attributeMetadata, bool remarked)
        {
            string prefix = string.Empty;

            if (remarked)
            {
                prefix = COMMENT_PREFIX;
            }

            writer.WriteLine(prefix + $"// Unable to generate enum field using display name for: {attributeMetadata.LogicalName}. Please refer to logical name for property access.");
        }

        private static bool IsValidProperty(MethodInfo methodInfo)
        {
            if (methodInfo == null)
            {
                return false;
            }

            if (methodInfo.IsPublic ||
                methodInfo.IsFamily)
            {
                return true;
            }

            return false;
        }

        private static bool IsValidField(FieldInfo fieldInfo)
        {
            if (fieldInfo == null ||
                fieldInfo.IsSpecialName)
            {
                return false;
            }

            if (fieldInfo.IsPublic ||
                fieldInfo.IsFamily)
            {
                return true;
            }

            return false;
        }

        private static bool IsValidMethod(MethodInfo methodInfo)
        {
            return IsValidProperty(methodInfo) &&
                !methodInfo.IsSpecialName;
        }
    }

    public class AttributeMetadataWithPropertyName
    {
        public string PropertyName { get; set; }
        public AttributeMetadata Value { get; set; }
    }

    public class ClassPropertyNameComparer : IComparer<AttributeMetadataWithPropertyName>
    {
        public int Compare(AttributeMetadataWithPropertyName x, AttributeMetadataWithPropertyName y)
        {
            int result;

            if (x.PropertyName == y.PropertyName)
            {
                result = 0;
            }
            else if (x.PropertyName == null)
            {
                result = 1;
            }
            else if (y.PropertyName == null)
            {
                result = -1;
            }
            else
            {
                result = Comparer<string>.Default.Compare(x.PropertyName, y.PropertyName);
            }

            if (result == 0)
            {
                var am1 = x.Value;
                var am2 = y.Value;

                bool? isCustom1 = am1.IsCustomAttribute;
                bool? isCustom2 = am2.IsCustomAttribute;

                if (isCustom1 == isCustom2)
                {
                    result = 0;
                }
                else if (isCustom1 == null)
                {
                    result = 1;
                }
                else if (isCustom2 == null)
                {
                    result = -1;
                }
                else
                {
                    result = Comparer<bool>.Default.Compare(isCustom2.Value, isCustom1.Value);
                }

                if (result == 0)
                {
                    var label1 = am1.DisplayName?.UserLocalizedLabel?.Label;
                    var label2 = am2.DisplayName?.UserLocalizedLabel?.Label;

                    if (label1 == label2)
                    {
                        result = 0;
                    }
                    else if (label2 == null)
                    {
                        result = -1;
                    }
                    else if (label1 == null)
                    {
                        result = 1;
                    }
                    else
                    {
                        result = Comparer<string>.Default.Compare(label1, label2);
                    }

                    if (result == 0)
                    {
                        result = Comparer<string>.Default.Compare(am1.LogicalName, am2.LogicalName);
                    }
                }
            }

            return result;
        }
    }

    public class OptionMetadataWithLabel
    {
        public string Label { get; set; }
        public OptionMetadata Value { get; set; }
    }

    public class OptionMetadataComparer : IComparer<OptionMetadataWithLabel>
    {
        public int Compare(OptionMetadataWithLabel x, OptionMetadataWithLabel y)
        {
            int result;

            if (x.Label == y.Label)
            {
                result = 0;
            }
            else if (x.Label == null)
            {
                result = 1;
            }
            else if (y.Label == null)
            {
                result = -1;
            }
            else
            {
                result = Comparer<string>.Default.Compare(x.Label, y.Label);
            }

            if (result == 0)
            {
                var om1 = x.Value;
                var om2 = y.Value;

                var label1 = om1.Label?.UserLocalizedLabel?.Label;
                var label2 = om2.Label?.UserLocalizedLabel?.Label;

                if (label1 == label2)
                {
                    result = 0;
                }
                else if (label1 == null)
                {
                    result = 1;
                }
                else if (label2 == null)
                {
                    result = -1;
                }
                else
                {
                    result = Comparer<string>.Default.Compare(label1, label2);
                }

                if (result == 0)
                {
                    int? value1 = om1.Value;
                    int? value2 = om2.Value;

                    if (value1 == value2)
                    {
                        result = 0;
                    }
                    else if (value1 == null)
                    {
                        result = 1;
                    }
                    else if (value2 == null)
                    {
                        result = -1;
                    }
                    else
                    {
                        result = Comparer<int>.Default.Compare(value1.Value, value2.Value);
                    }
                }
            }

            return result;
        }
    }
}
