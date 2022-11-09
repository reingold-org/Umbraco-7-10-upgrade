using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Hosting;
using System.Web.Mvc;
using Archetype.Models;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using nuPickers.Shared.DotNetDataSource;
using nuPickers.Shared.Editor;
using nuPickers.Shared.JsonDataSource;
using Our.Umbraco.UnVersion;
using Our.Umbraco.UnVersion.Services;
using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Services;
using Umbraco.Web;
using Umbraco.Web.Mvc;


public class MigrationController : SurfaceController
{
    const string ARCHETYPE_PROPERTY_EDITOR = "Imulus.Archetype";
    const string LINKPICKER_PROPERTY_EDITOR = "Gibe.LinkPicker";
    const string LINKPICKER_REPLACEMENT_DATATYPE_NAME = "Single Url Picker";
    const string NESTED_CONTENT_PROPERTY_EDITOR = "Umbraco.NestedContent";
    const string CONTENT_TYPE_FOLDER_NAME = "Blocks";
    const string DATA_TYPE_FOLDER_NAME = "Nested Content";
    const string TAB_NAME = "Content";
    const string PROPERTY_SUFFIX = "Old";
    const string NESTING_CONTENTLY = "NestingContently";

    //nupickers
    const string NUPICKERS_DATA_TYPE_FOLDER_NAME = "Pickers";
    //map nupicker editors to umbraco editors
    private readonly Dictionary<string, string> nupickerEditorMap = new Dictionary<string, string>()
    {
        { "nuPickers.DotNetDropDownPicker", "Umbraco.DropDown.Flexible" },
        { "nuPickers.JsonDropDownPicker", "Umbraco.DropDown.Flexible" },
        { "nuPickers.DotNetRadioButtonPicker", "Umbraco.RadioButtonList" },
        { "nuPickers.DotNetTypeaheadListPicker", "Dawoe.OEmbedPickerPropertyEditor"}
    };

    //collection of property editors that we need to convert to UDI format
    private readonly List<string> udiConversions = new List<string> 
    { 
        "Umbraco.MediaPicker2", 
        "Umbraco.MultiNodeTreePicker2",
        "Umbraco.ContentPicker2"
    };

    private readonly IUnVersionService unVersionService;
    private readonly IDataTypeService dataTypeService;
    private readonly IContentTypeService contentTypeService;
    private readonly IContentService contentService;
    private readonly IMediaService mediaService;

    private readonly int dataTypeFolder;
    private readonly int contentTypeFolder;
    private readonly int nupickersDataTypeFolder;

    public MigrationController()
    {
        //this.unVersionService = unVersionService;
        dataTypeService = ApplicationContext.Services.DataTypeService;
        contentTypeService = ApplicationContext.Services.ContentTypeService;
        contentService = ApplicationContext.Services.ContentService;
        mediaService = ApplicationContext.Services.MediaService;
        unVersionService = UnVersionContext.Instance.UnVersionService;

        //make sure we have folders for our content/data types
        dataTypeFolder = GetOrCreateDataTypeContainer(DATA_TYPE_FOLDER_NAME);
        contentTypeFolder = GetOrCreateContentTypeContainer(CONTENT_TYPE_FOLDER_NAME);
        nupickersDataTypeFolder = GetOrCreateDataTypeContainer(NUPICKERS_DATA_TYPE_FOLDER_NAME);
    }


    public ActionResult Index()
    {
        return Content("Run the appropriate action for your task");
    }

    /// <summary>
    /// List all data types implementing archetype
    /// </summary>
    /// <returns></returns>
    public ActionResult ListArchetypes()
    {
        var datatypes = dataTypeService.GetDataTypeDefinitionByPropertyEditorAlias(ARCHETYPE_PROPERTY_EDITOR);

        //Dictionary<string, object> stuff = new Dictionary<string, object>();
        List<object> stuff = new List<object>();

        foreach (var datatype in datatypes)
        {

            var prevalueJson = dataTypeService.GetPreValuesByDataTypeId(datatype.Id).First();
            var prevalue = JsonConvert.DeserializeObject<ArchetypePreValue>(prevalueJson);

            //get datatype name of property example
            //dataTypeService.GetDataTypeDefinitionById(y.Fieldsets.First().Properties.First().DataTypeGuid).Name,

            //loop through properties and determine if any are archetypes

            bool isContainer = prevalue.Fieldsets.Count() > 1;
            bool containsArchetype = false;
            foreach (var fieldset in prevalue.Fieldsets)
            {
                foreach (var prop in fieldset.Properties)
                {
                    var propEditor = dataTypeService.GetDataTypeDefinitionById(prop.DataTypeGuid);
                    if (propEditor?.PropertyEditorAlias == ARCHETYPE_PROPERTY_EDITOR)
                    {
                        containsArchetype = true;
                    }
                }
            }


            //stuff.Add(datatype.Name, y);
            stuff.Add(new
            {
                Name = datatype.Name,
                Id = datatype.Id,
                IsContainer = isContainer,
                ContainsArchetype = containsArchetype,
                Fieldsets = prevalue.Fieldsets,
            });
        }


        var json = datatypes.Select(x => new
        {

        });

        return Json(stuff, JsonRequestBehavior.AllowGet);
    }

    /// <summary>
    /// Remove all of the Content/Data types that were created, for testing
    /// </summary>
    /// <returns></returns>
    public ActionResult CleanUpDataTypes()
    {
        int dataTypes = 0;
        int contentTypes = 0;

        foreach(var item in dataTypeService.GetAllDataTypeDefinitions())
        {
            if(item.ParentId == dataTypeFolder || item.ParentId == nupickersDataTypeFolder)
            {
                dataTypeService.Delete(item);
                dataTypes++;
            }
        }

        foreach(var item in contentTypeService.GetAllContentTypes())
        {
            if(item.ParentId == contentTypeFolder)
            {
                contentTypeService.Delete(item);
                contentTypes++;
            }
        }

        return Json(new { dataTypes, contentTypes }, JsonRequestBehavior.AllowGet);
    }

    #region Step 1 - Trim content versions

    /// <summary>
    /// Run UnVersion against all content to trim versions
    /// </summary>
    /// <returns></returns>
    public ActionResult TrimContentVersions()
    {
        var roots = contentService.GetRootContent();
        foreach(var root in roots)
        {
            unVersionService.UnVersion(root);

            foreach(var content in contentService.GetDescendants(root))
            {
                unVersionService.UnVersion(content);
            }
        }

        return Content("done");
    }

    #endregion

    #region Step 1.5 - List Published Items

    public ActionResult GetPublishedPages()
    {
        var publishedPages = new List<int>();

        var roots = contentService.GetRootContent();
        foreach (var root in roots)
        {
            var descendents = contentService.GetDescendants(root);
            var pages = new List<IContent>() { root }.Concat(descendents);

            foreach (var page in pages)
            {
                if(page.Published)
                {
                    publishedPages.Add(page.Id);
                }

            }
        }

        return Json(publishedPages, JsonRequestBehavior.AllowGet);
    }

    #endregion

    #region Step 2 - Update obsolete datatypes

    /// <summary>
    /// Iterates through all datatypes and updates obsoleted propertyeditors
    /// </summary>
    /// <returns></returns>
    public ActionResult UpdateObsoleteDatatypes()
    {
        Dictionary<string, string> propertyTypes = new Dictionary<string, string>
        {
            { "Umbraco.ContentPickerAlias", "Umbraco.ContentPicker2" },
            { "Umbraco.MultiNodeTreePicker", "Umbraco.MultiNodeTreePicker2" },
            { "Umbraco.MediaPicker", "Umbraco.MediaPicker2" },
            { "Umbraco.MemberPicker", "Umbraco.MemberPicker2" },
            { "Umbraco.MultipleMediaPicker", "Umbraco.MediaPicker2" },
            { "Umbraco.RelatedLinks", "Umbraco.RelatedLinks2" }
        };

        Dictionary<string, List<string>> updatedDataTypes = new Dictionary<string, List<string>>
        {
            { "Umbraco.ContentPickerAlias", new List<string>() },
            { "Umbraco.MultiNodeTreePicker", new List<string>() },
            { "Umbraco.MediaPicker", new List<string>() },
            { "Umbraco.MemberPicker", new List<string>() },
            { "Umbraco.MultipleMediaPicker", new List<string>() },
            { "Umbraco.RelatedLinks", new List<string>() }
        };

        var datatypes = dataTypeService.GetAllDataTypeDefinitions();
        
        foreach(var datatype in datatypes)
        {
            var propertyEditor = datatype.PropertyEditorAlias;
            
            if (propertyTypes.ContainsKey(propertyEditor))
            {
                datatype.PropertyEditorAlias = propertyTypes[propertyEditor];
                datatype.DatabaseType = DataTypeDatabaseType.Nvarchar;
                dataTypeService.Save(datatype);

                updatedDataTypes[propertyEditor].Add(datatype.Name);
            }
        }

        var json = new { updatedDataTypes };

        return Json(json, JsonRequestBehavior.AllowGet);
    }



    #endregion       

    #region Step 2.5 - Rename Content types and Data types where needed for consistency

    /// <summary>
    /// Renames Document and Data types
    /// </summary>
    /// <returns></returns>
    public ActionResult UpdateContentAndDataTypeNames()
    {
        Dictionary<string, string> dataTypesToRename = new Dictionary<string, string>()
        {
            { "Content Sub-Previews", "Subcontent Previews" },
            { "Featured Links", "Featured Links" } //we just want to resave this
        };

        Dictionary<string, string> contentTypesToRename = new Dictionary<string, string>()
        {
            { "videoGallery", "Video Gallery Container" }
        };

        foreach (var type in dataTypesToRename)
        {
            var datatype = dataTypeService.GetDataTypeDefinitionByName(type.Key);
            datatype.Name = type.Value;
            dataTypeService.Save(datatype);
        }

        foreach (var type in contentTypesToRename)
        {
            var contentType = contentTypeService.GetContentType(type.Key);
            contentType.Alias = type.Value.ToSafeAlias(true);
            contentType.Name = type.Value;
            contentTypeService.Save(contentType);
        }

        return Content("done");
    }

    #endregion

    #region Step 3 - Convert Archetypes to ContentTypes

    public ActionResult ConvertArchetypes()
    {
        List<string> isContainer = new List<string>();
        List<string> existingContentTypes = new List<string>();
        List<string> containsArchetype = new List<string>();
        List<string> created = new List<string>();
        List<string> hadError = new List<string>();

        //find all top-level ArcheTypes
        var datatypes = dataTypeService.GetDataTypeDefinitionByPropertyEditorAlias(ARCHETYPE_PROPERTY_EDITOR);

        //list for holding datatypes that reference archetypes so we can process them in a second pass
        var typesThatReferenceArchetypes = new List<IDataTypeDefinition>();

        var nestedFieldSetsThatReferenceArchetypes = new List<ArchetypePreValueFieldset>();

        //list for holding datatypes that contain nested types
        var containerTypes = new List<IDataTypeDefinition>();

        //list for holding datatypes that reference container types
        var referencesContainerType = new List<IDataTypeDefinition>();

        //first pass - Process datatypes that do not contain archetypes, or reference archetypes since we will create those later
        foreach (var datatype in datatypes)
        {
            //get the prevalue data which is where properties are defined
            var prevalueJson = dataTypeService.GetPreValuesByDataTypeId(datatype.Id).First();
            var prevalue = JsonConvert.DeserializeObject<ArchetypePreValue>(prevalueJson);

            //if archetype is for a single element, create the content type and a nested content data type to hold it
            if (prevalue.Fieldsets.Count() == 1)
            {
                var fieldset = prevalue.Fieldsets.First();

                //check if a doctype with this alias already exists. If not we can try to create one
                var contentTypeAlias = datatype.Name.ToSafeAlias(true); //fieldset.Alias;
                var contentTypeName = datatype.Name; //fieldset.Label;
                var existingContentType = contentTypeService.GetContentType(contentTypeAlias);

                if (existingContentType == null)
                {
                    var hasArchetype = false;

                    //determine if any of the properties are nested archetypes. If so, process at the end.
                    foreach (var prop in fieldset.Properties)
                    {
                        var propEditor = dataTypeService.GetDataTypeDefinitionById(prop.DataTypeGuid);
                        if (propEditor?.PropertyEditorAlias == ARCHETYPE_PROPERTY_EDITOR)
                        {
                            hasArchetype = true;
                        }
                    }

                    if (!hasArchetype)
                    {
                        //try creating the content type
                        if (createContentTypeFromArchetype(contentTypeName, contentTypeAlias, fieldset.Properties, FirstWithValue(fieldset.LabelTemplate, fieldset.Label), prevalue.MaxFieldsets))
                        {
                            created.Add(contentTypeName);
                        }
                        else
                        {
                            hadError.Add(contentTypeName);
                        }
                    }
                    else
                    {
                        //save this one for later processing
                        containsArchetype.Add(contentTypeName);
                        typesThatReferenceArchetypes.Add(datatype);
                    }
                }
                else
                {
                    existingContentTypes.Add(contentTypeAlias);
                }
            }
            else
            {
                isContainer.Add(datatype.Name);
                containerTypes.Add(datatype);

                foreach (var fieldset in prevalue.Fieldsets)
                {
                    //check if a doctype with this alias already exists. If not we can try to create one
                    var contentTypeAlias = fieldset.Alias; //we use the fieldset's alias instead of the data type's this time
                    var existingContentType = contentTypeService.GetContentType(contentTypeAlias);

                    if (existingContentType == null)
                    {
                        var hasArchetype = false;

                        //determine if any of the properties are nested archetypes. If so, process at the end.
                        foreach (var prop in fieldset.Properties)
                        {
                            var propEditor = dataTypeService.GetDataTypeDefinitionById(prop.DataTypeGuid);
                            if (propEditor?.PropertyEditorAlias == ARCHETYPE_PROPERTY_EDITOR)
                            {
                                hasArchetype = true;
                            }
                        }

                        if (!hasArchetype)
                        {
                            //try creating the content type
                            if (createContentTypeFromArchetype(fieldset.Label, contentTypeAlias, fieldset.Properties, FirstWithValue(fieldset.LabelTemplate, fieldset.Label), prevalue.MaxFieldsets))
                            {
                                created.Add(fieldset.Label);
                            }
                            else
                            {
                                hadError.Add(fieldset.Label);
                            }
                        }
                        else
                        {
                            //save this one for later processing
                            containsArchetype.Add(fieldset.Label);
                            nestedFieldSetsThatReferenceArchetypes.Add(fieldset);
                        }
                    }
                }

            }
        }
        
        //second pass - process the types that reference an archetype 
        foreach (var datatype in typesThatReferenceArchetypes)
        {
            bool referencesContainer = false;

            //get the prevalue data which is where properties are defined
            var prevalueJson = dataTypeService.GetPreValuesByDataTypeId(datatype.Id).First();
            var prevalue = JsonConvert.DeserializeObject<ArchetypePreValue>(prevalueJson);

            //we know there's only fieldset for this one
            var fieldset = prevalue.Fieldsets.First();

            //validate that any referenced archetypes have been created
            foreach (var prop in fieldset.Properties)
            {
                var propEditor = dataTypeService.GetDataTypeDefinitionById(prop.DataTypeGuid);
                if (propEditor?.PropertyEditorAlias == ARCHETYPE_PROPERTY_EDITOR)
                {
                    var alias = propEditor.Name.ToSafeAlias();
                    var ct = contentTypeService.GetContentType(alias);
                    if(ct == null)
                    {
                        //we haven't created a content type for this referenced archetype yet (probably a container type)
                        referencesContainerType.Add(datatype);
                        referencesContainer = true;
                    }

                }
            }

            if(!referencesContainer)
            {
                //check if a doctype with this alias already exists. If not we can try to create one
                var contentTypeAlias = datatype.Name.ToSafeAlias(true); //fieldset.Alias; // 
                var contentTypeName = datatype.Name; //fieldset.Label;  //

                var existingContentType = contentTypeService.GetContentType(contentTypeAlias);

                if (existingContentType == null)
                {
                    //try creating the content type
                    if (createContentTypeFromArchetype(contentTypeName, contentTypeAlias, fieldset.Properties, FirstWithValue(fieldset.LabelTemplate, fieldset.Label), prevalue.MaxFieldsets))
                    {
                        created.Add(contentTypeName);
                    }
                    else
                    {
                        hadError.Add(contentTypeName);
                    }
                }
                else
                {
                    existingContentTypes.Add(fieldset.Alias);
                }
            }
        }
        
        //third pass - process the fieldsets from container types that reference an archetype
        foreach (var fieldset in nestedFieldSetsThatReferenceArchetypes)
        {
            //check if a doctype with this alias already exists. If not we can try to create one
            var contentTypeAlias = fieldset.Alias; //we use the fieldset's alias instead of the data type's this time
            var existingContentType = contentTypeService.GetContentType(contentTypeAlias);

            if (existingContentType == null)
            {
                //try creating the content type
                if (createContentTypeFromArchetype(fieldset.Label, contentTypeAlias, fieldset.Properties, FirstWithValue(fieldset.LabelTemplate, fieldset.Label)))
                {
                    created.Add(fieldset.Label);
                }
                else
                {
                    hadError.Add(fieldset.Label);
                }
            }
        }

        //fourth pass - process the containter types to create nested data types
        foreach (var datatype in containerTypes)
        {
            //get or create the nest content data type
            var dataTypeName = string.Format("{0} - {1}", DATA_TYPE_FOLDER_NAME, datatype.Name);

            //get the prevalue data which is where properties are defined
            var prevalueJson = dataTypeService.GetPreValuesByDataTypeId(datatype.Id).First();
            var prevalue = JsonConvert.DeserializeObject<ArchetypePreValue>(prevalueJson);

            var contentTypes = prevalue.Fieldsets.ToDictionary(x => x.Alias, x => FirstWithValue(x.LabelTemplate, x.Label));

            var dataType = GetOrCreateNestedDataType(dataTypeName, contentTypes, prevalue.MaxFieldsets);
        }

        //finally, process the types that reference container types
        foreach(var datatype in referencesContainerType)
        {
            //get the prevalue data which is where properties are defined
            var prevalueJson = dataTypeService.GetPreValuesByDataTypeId(datatype.Id).First();
            var prevalue = JsonConvert.DeserializeObject<ArchetypePreValue>(prevalueJson);

            //we know there's only fieldset for this one
            var fieldset = prevalue.Fieldsets.First();

            //check if a doctype with this alias already exists. If not we can try to create one
            var contentTypeAlias = datatype.Name.ToSafeAlias(true); //fieldset.Alias; // 
            var contentTypeName = datatype.Name; //fieldset.Label;  //

            var existingContentType = contentTypeService.GetContentType(contentTypeAlias);

            if (existingContentType == null)
            {
                //try creating the content type
                if (createContentTypeFromArchetype(contentTypeName, contentTypeAlias, fieldset.Properties, FirstWithValue(fieldset.LabelTemplate, fieldset.Label), prevalue.MaxFieldsets))
                {
                    created.Add(contentTypeName);
                }
                else
                {
                    hadError.Add(contentTypeName);
                }
            }
            else
            {
                existingContentTypes.Add(fieldset.Alias);
            }
        }
        
        var json = new
        {
            created,
            hadError,
            containsArchetype = containsArchetype.OrderBy(x => x),
            existingContentTypes = existingContentTypes.OrderBy(x => x),
            isContainer = isContainer.OrderBy(x => x)
        };

        return Json(json, JsonRequestBehavior.AllowGet);
    }

    private bool createContentTypeFromArchetype(string name, string alias, IEnumerable<ArchetypePreValueProperty> properties, string labelTemplate = null, int maxItems = 0)
    {
        try
        {
            var contentType = new ContentType(contentTypeFolder);

            contentType.Alias = alias;
            contentType.Name = name;

            //create nestedcontently property
            var nestedContentlyDataType = dataTypeService.GetDataTypeDefinitionByName(NESTING_CONTENTLY);
            if(nestedContentlyDataType != null)
            {
                var nestedContently = new PropertyType(nestedContentlyDataType);
                nestedContently.Alias = "umbracoNaviHide";
                nestedContently.Name = "Hide?";
                contentType.AddPropertyType(nestedContently, TAB_NAME);
            }

            foreach (var prop in properties)
            {
                var propEditor = dataTypeService.GetDataTypeDefinitionById(prop.DataTypeGuid);
                if (propEditor != null)
                {
                    PropertyType propType;

                    if (propEditor.PropertyEditorAlias == ARCHETYPE_PROPERTY_EDITOR)
                    {
                        //get or create the nested content data type
                        IDataTypeDefinition dataType;

                        //determine if property is a nested archetype
                        var prevalueJson = dataTypeService.GetPreValuesByDataTypeId(propEditor.Id).First();
                        var prevalue = JsonConvert.DeserializeObject<ArchetypePreValue>(prevalueJson);
                        
                        if (prevalue.Fieldsets.Count() > 1)
                        {
                            var dataTypeName = string.Format("{0} - {1}", DATA_TYPE_FOLDER_NAME, propEditor.Name/*name*/);
                            dataType = GetOrCreateNestedDataType(dataTypeName,
                                prevalue.Fieldsets.ToDictionary(x => x.Alias, x => FirstWithValue(x.LabelTemplate, x.Label)),
                                prevalue.MaxFieldsets);
                        }
                        else
                        {
                            var fieldset = prevalue.Fieldsets.FirstOrDefault();
                            var dataTypeName = string.Format("{0} - {1}", DATA_TYPE_FOLDER_NAME, propEditor.Name);
                            dataType = GetOrCreateNestedDataType(dataTypeName,
                                new Dictionary<string, string> {{ propEditor.Name.ToSafeAlias(true),
                                    FirstWithValue(fieldset.LabelTemplate, fieldset.Label) }},
                                prevalue.MaxFieldsets);
                        }

                        propType = new PropertyType(dataType, prop.Alias);
                    }
                    else
                    {
                        propType = new PropertyType(propEditor, prop.Alias);
                    }

                    propType.Name = prop.Label;
                    propType.Description = prop.HelpText;
                    propType.Mandatory = prop.Required;
                    propType.ValidationRegExp = prop.RegEx;

                    contentType.AddPropertyType(propType, TAB_NAME);
                }
            }

            contentTypeService.Save(contentType);

            //create a datatype to hold this
            var dtName = string.Format("{0} - {1}", DATA_TYPE_FOLDER_NAME, contentType.Name);
            var dt = GetOrCreateNestedDataType(dtName,
                new Dictionary<string, string> { { contentType.Alias, FirstWithValue(labelTemplate, contentType.Name) } },
                maxItems);

            return true;
        }
        catch
        {
            //not sure what to do here...cry?
            return false;
        }
    }

    private IDataTypeDefinition GetOrCreateNestedDataType(string name, Dictionary<string, string> contentTypes, int maxItems = 0)
    {
        IDataTypeDefinition dataType;

        dataType = dataTypeService.GetDataTypeDefinitionByName(name);

        if (dataType == null)
        {
            //create a nested content data type
            dataType = new DataTypeDefinition(dataTypeFolder, NESTED_CONTENT_PROPERTY_EDITOR);
            dataType.Name = name;

            var prevalues = new Dictionary<string, PreValue>();

            var types = contentTypes.Select(x => new
            {
                ncAlias = x.Key,
                ncTabAlias = TAB_NAME,
                nameTemplate = Regex.Replace(x.Value, "archpreview\\((\\w+)\\)", "$1 | ncRichText") //handle rte fields
            });

            prevalues.Add("contentTypes", new PreValue(JsonConvert.SerializeObject(types)));
            prevalues.Add("minItems", new PreValue("0"));
            prevalues.Add("maxItems", new PreValue(maxItems.ToString()));
            prevalues.Add("confirmDeletes", new PreValue("1"));
            prevalues.Add("showIcons", new PreValue("1"));
            prevalues.Add("hideLabel", new PreValue("0"));

            dataTypeService.SaveDataTypeAndPreValues(dataType, prevalues);
        }
        else
        {

        }

        return dataType;
    }

    #endregion

    #region Step 4 - Convert nupicker datatypes

    public ActionResult ConvertNupickerDataTypes()
    {
        var datatypes = dataTypeService.GetAllDataTypeDefinitions();

        foreach (var datatype in datatypes)
        {
            var propertyEditor = datatype.PropertyEditorAlias;

            //if we find one of our target property editors, create a new data type with the replacement
            if (nupickerEditorMap.ContainsKey(propertyEditor))
            {
                var oldPrevalues = dataTypeService.GetPreValuesCollectionByDataTypeId(datatype.Id);

                IEnumerable<string> values = new List<string>();
                //try to convert data source to a model so we can extract values
                var dotNetDataSourceModel = JsonConvert.DeserializeObject<DotNetDataSource>(oldPrevalues.PreValuesAsDictionary["dataSource"].Value);
                if (dotNetDataSourceModel.AssemblyName != null)
                {
                    IDotNetDataSource dotNetDataSource = AppDomain.CurrentDomain.CreateInstanceAndUnwrap(GetAssembly(dotNetDataSourceModel.AssemblyName).FullName, dotNetDataSourceModel.ClassName) as IDotNetDataSource;
                    if (dotNetDataSource != null)
                    {
                        values = dotNetDataSource.GetEditorDataItems(0)?.Select(x => x.Key);
                    }
                }
                else
                {
                    var datasource = JsonConvert.DeserializeObject<JsonDataSource>(oldPrevalues.PreValuesAsDictionary["dataSource"].Value);
                    var jsonData = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(GetDataFromUrl(datasource.Url));
                    values = jsonData.Select(x => x["hexcode"]);
                }

                var name = string.Format("{0} - {1}", NUPICKERS_DATA_TYPE_FOLDER_NAME, datatype.Name);
                IDataTypeDefinition newDataType;
                newDataType = dataTypeService.GetDataTypeDefinitionByName(name);

                //check if datatype exists
                if (newDataType == null)
                {
                    //create new datatype
                    newDataType = new DataTypeDefinition(nupickersDataTypeFolder, nupickerEditorMap[propertyEditor]);
                    newDataType.Name = name;
                }

                var prevalues = new Dictionary<string, PreValue>();

                if (!values.IsNullOrEmpty())
                {
                    for (var i = 0; i < values.Count(); i++)
                    {
                        prevalues.Add(i.ToString(), new PreValue(values.ElementAt(i)));
                    }

                    if (nupickerEditorMap[propertyEditor] == "Umbraco.DropDown.Flexible")
                    {
                        prevalues.Add("multiple", new PreValue("0"));
                    }
                }

                dataTypeService.SaveDataTypeAndPreValues(newDataType, prevalues);

            }
        }

        var json = new { };
        return Json(json, JsonRequestBehavior.AllowGet);
    }

    #endregion

    #region Step 5 - Create nupicker properties

    public ActionResult CreateNupickerProperties()
    {
        Dictionary<string, List<string>> errors = new Dictionary<string, List<string>>();
        Dictionary<string, List<string>> created = new Dictionary<string, List<string>>();

        var contentTypes = contentTypeService.GetAllContentTypes();

        foreach (var contentType in contentTypes)
        {
            created.Add(contentType.Alias, new List<string>());
            errors.Add(contentType.Alias, new List<string>());

            var propsToRename = new List<PropertyType>();
            var propsToConvert = new Dictionary<string, string>();

            //loop through property groups instead of properties directly that way we create new fields in the same tab
            foreach (var propertyGroup in contentType.PropertyGroups)
            {
                foreach (var propertyType in propertyGroup.PropertyTypes)
                {
                    if (nupickerEditorMap.ContainsKey(propertyType.PropertyEditorAlias))
                    {
                        //add the property to the list of ones to process
                        propsToRename.Add(propertyType);
                        propsToConvert.Add(propertyType.Alias, propertyGroup.Name);
                    }
                }
            }

            //look at properties with no group
            foreach (var propertyType in contentType.NoGroupPropertyTypes)
            {
                if (nupickerEditorMap.ContainsKey(propertyType.PropertyEditorAlias))
                {
                    //add the property to the list of ones to process
                    propsToRename.Add(propertyType);
                    propsToConvert.Add(propertyType.Alias, null);
                }
            }

            if (propsToRename.Any())
            {
                //first process the list to rename old properies
                foreach (var prop in propsToRename)
                {
                    //skip ones that have already been renamed 
                    if (!prop.Alias.EndsWith(PROPERTY_SUFFIX))
                    {
                        var newAlias = string.Format("{0}{1}", prop.Alias, PROPERTY_SUFFIX);
                        prop.Alias = newAlias;
                    }
                }

                //save the content type with renamed properties
                contentTypeService.Save(contentType);

                //reload the content type after saving so we don't get a conflict when adding new property with old alias
                var updatedContentType = contentTypeService.GetContentType(contentType.Id);

                //process the list of properties again to add new ones
                foreach (var item in propsToConvert)
                {
                    var tab = item.Value;
                    var alias = item.Key;

                    //get the propery from the content type again to ensure it's fresh
                    var oldProp = updatedContentType.PropertyTypes.First(x => x.Alias == string.Format("{0}{1}", alias, PROPERTY_SUFFIX));

                    try
                    {
                        //we have an archetype property, so create a new one to hold our new content type
                        //first figure out what kind of archetype we're working with
                        var oldType= dataTypeService.GetDataTypeDefinitionById(oldProp.DataTypeDefinitionId);
                        var datatypeName = string.Format("{0} - {1}", NUPICKERS_DATA_TYPE_FOLDER_NAME, oldType.Name);
                        var datatype = dataTypeService.GetDataTypeDefinitionByName(datatypeName);

                        if (datatype != null)
                        {
                            //create new property type
                            var newProp = new PropertyType(datatype, alias);
                            newProp.Name = oldProp.Name;
                            newProp.Description = oldProp.Description ?? string.Empty;
                            newProp.Mandatory = oldProp.Mandatory;
                            newProp.ValidationRegExp = oldProp.ValidationRegExp;
                            newProp.SortOrder = oldProp.SortOrder;

                            //add the new type to our content type
                            updatedContentType.AddPropertyType(newProp, tab);

                            //debug output
                            created[updatedContentType.Alias].Add(alias);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(typeof(MigrationController), e.Message, e);
                        errors[contentType.Alias].Add(alias);
                    }
                }

                //save the content type with new properties
                try
                {
                    contentTypeService.Save(updatedContentType);
                }
                catch (Exception e)
                {
                    Logger.Error(typeof(MigrationController), e.Message, e);
                }
            }
        }

        var json = new { errors, created };
        return Json(json, JsonRequestBehavior.AllowGet);
    }

    #endregion

    #region Step 6 - Copy nupicker data

    public ActionResult CopyNupickerData()
    {
        List<KeyValuePair<string, string>> errors = new List<KeyValuePair<string, string>>();

        var roots = contentService.GetRootContent();
        foreach (var root in roots)
        {
            var descendents = contentService.GetDescendants(root);
            var pages = new List<IContent>() { root }.Concat(descendents);

            foreach (var page in pages)
            {
                foreach (var property in page.Properties.Where(x => nupickerEditorMap.ContainsKey(x.PropertyType.PropertyEditorAlias)))
                {
                    //check if we can find a property without the suffix we added
                    var alias = property.Alias.Remove(property.Alias.IndexOf(PROPERTY_SUFFIX));

                    var newProperty = page.Properties.FirstOrDefault(x => x.Alias == alias);
                    if (newProperty != null)
                    {
                        //now check that the data type is correct
                        var datatype = dataTypeService.GetDataTypeDefinitionById(newProperty.PropertyType.DataTypeDefinitionId);
                        var archetype = dataTypeService.GetDataTypeDefinitionById(property.PropertyType.DataTypeDefinitionId);
                        var datatypeName = string.Format("{0} - {1}", NUPICKERS_DATA_TYPE_FOLDER_NAME, archetype.Name);
                        if (datatype.Name == datatypeName)
                        {
                            //now we can copy data
                            try
                            {
                                var data = JsonConvert.DeserializeObject<List<EditorDataItem>>(property.Value.ToString());
                                object value = null;

                                if (datatype.PropertyEditorAlias == "Umbraco.RadioButtonList" || datatype.PropertyEditorAlias == "Umbraco.DropDown.Flexible")
                                {
                                    var prevalues = dataTypeService.GetPreValuesCollectionByDataTypeId(datatype.Id);
                                    value = prevalues.PreValuesAsDictionary.Where(x => x.Value.Value == data.FirstOrDefault().Key).FirstOrDefault().Value.Value; //umbraco 8+ needs the value, not the id
                                }
                                if (datatype.PropertyEditorAlias == "Umbraco.RadioButtonList")
                                {
                                    var prevalues = dataTypeService.GetPreValuesCollectionByDataTypeId(datatype.Id);
                                    //Umbraco 8+ needs the value not the Id
                                    value = prevalues.PreValuesAsDictionary.Where(x => x.Value.Value == data.FirstOrDefault().Key).FirstOrDefault().Value?.Value;
                                }
                                else if (datatype.PropertyEditorAlias == "Umbraco.DropDown.Flexible")
                                {
                                    var prevalues = dataTypeService.GetPreValuesCollectionByDataTypeId(datatype.Id);
                                    //Umbraco 8+ needs the value not the Id, also wants the value in an array
                                    value = JsonConvert.SerializeObject(new List<string>() { prevalues.PreValuesAsDictionary.Where(x => x.Value.Value == data.FirstOrDefault().Key).FirstOrDefault().Value?.Value });
                                }

                                else if (datatype.PropertyEditorAlias == "Dawoe.OEmbedPickerPropertyEditor")
                                {
                                    //shove the video id into an OEmbed object
                                    value = ConvertOEmbed(property.Value);
                                }

                                page.SetValue(newProperty.Alias, value);

                                //save page
                                contentService.Save(page);
                            }
                            catch (Exception e)
                            {
                                Logger.Error(typeof(MigrationController), string.Format("Error copying data for page: {0}, property: {1}", page.Id, alias), e);
                                errors.Add(new KeyValuePair<string, string>(page.Id.ToString(), property.Alias));
                            }
                        }
                    }
                }
            }
        }

        var json = new { errors };
        return Json(json, JsonRequestBehavior.AllowGet);
    }

    #endregion

    #region Step 7 - Add properties to ContentTypes

    /// <summary>
    /// Update content types using Archetype properties to instead use nested content
    /// </summary>
    /// <returns></returns>
    public ActionResult UpdateContentTypes()
    {
        Dictionary<string, List<string>> errors = new Dictionary<string, List<string>>();
        Dictionary<string, List<string>> created = new Dictionary<string, List<string>>();

        var contentTypes = contentTypeService.GetAllContentTypes();

        foreach (var contentType in contentTypes)
        {
            created.Add(contentType.Alias, new List<string>());
            errors.Add(contentType.Alias, new List<string>());

            var propsToRename = new List<PropertyType>();
            var propsToConvert = new Dictionary<string, string>();

            //loop through property groups instead of properties directly that way we create new fields in the same tab
            foreach (var propertyGroup in contentType.PropertyGroups)
            {
                foreach (var propertyType in propertyGroup.PropertyTypes)
                {
                    if (propertyType.PropertyEditorAlias == ARCHETYPE_PROPERTY_EDITOR || propertyType.PropertyEditorAlias == LINKPICKER_PROPERTY_EDITOR)
                    {
                        //add the property to the list of ones to process
                        propsToRename.Add(propertyType);
                        propsToConvert.Add(propertyType.Alias, propertyGroup.Name);
                    }
                }
            }

            //look at properties with no group
            foreach (var propertyType in contentType.NoGroupPropertyTypes)
            {
                if (propertyType.PropertyEditorAlias == ARCHETYPE_PROPERTY_EDITOR || propertyType.PropertyEditorAlias == LINKPICKER_PROPERTY_EDITOR)
                {
                    //add the property to the list of ones to process
                    propsToRename.Add(propertyType);
                    propsToConvert.Add(propertyType.Alias, null);
                }
            }

            if (propsToRename.Any())
            {
                //first process the list to rename old properies
                foreach (var prop in propsToRename)
                {
                    //skip ones that have already been renamed 
                    if (!prop.Alias.EndsWith(PROPERTY_SUFFIX))
                    {
                        var newAlias = string.Format("{0}{1}", prop.Alias, PROPERTY_SUFFIX);
                        prop.Alias = newAlias;
                    }
                }

                //save the content type with renamed properties
                contentTypeService.Save(contentType);

                //reload the content type after saving so we don't get a conflict when adding new property with old alias
                var updatedContentType = contentTypeService.GetContentType(contentType.Id);

                //process the list of properties again to add new ones
                foreach (var item in propsToConvert)
                {
                    var tab = item.Value;
                    var alias = item.Key;

                    //get the propery from the content type again to ensure it's fresh
                    var oldProp = updatedContentType.PropertyTypes.First(x => x.Alias == string.Format("{0}{1}", alias, PROPERTY_SUFFIX));

                    try
                    {
                        IDataTypeDefinition datatype;

                        if (oldProp.PropertyEditorAlias == LINKPICKER_PROPERTY_EDITOR)
                        {
                            //use single url picker instead of gibe.linkpicker

                            //try to find a datatype that uses the multiple url property
                            var pickerDt = dataTypeService.GetDataTypeDefinitionByName(LINKPICKER_REPLACEMENT_DATATYPE_NAME);
                            if (pickerDt == null)
                            {
                                //create it if it doesn't exit
                                pickerDt = new DataTypeDefinition("Umbraco.MultiUrlPicker");
                                pickerDt.Name = LINKPICKER_REPLACEMENT_DATATYPE_NAME;
                                var prevalues = new Dictionary<string, PreValue>();
                                prevalues.Add("maxItems", new PreValue("1"));

                                dataTypeService.SaveDataTypeAndPreValues(pickerDt, prevalues);
                            }

                            datatype = pickerDt;
                        }
                        else
                        {
                            //we have an archetype property, so create a new one to hold our new content type
                            //first figure out what kind of archetype we're working with
                            var archetype = dataTypeService.GetDataTypeDefinitionById(oldProp.DataTypeDefinitionId);

                            //try to get by archetype name first
                            var datatypeName = string.Format("{0} - {1}", DATA_TYPE_FOLDER_NAME, archetype.Name);
                            datatype = dataTypeService.GetDataTypeDefinitionByName(datatypeName);

                            if (datatype == null)
                            {
                                //try to get by fieldset name
                                //get the prevalue data which is where properties are defined
                                var prevalueJson = dataTypeService.GetPreValuesByDataTypeId(archetype.Id).First();
                                var prevalue = JsonConvert.DeserializeObject<ArchetypePreValue>(prevalueJson);

                                if (prevalue.Fieldsets.FirstOrDefault() != null)
                                {
                                    datatypeName = string.Format("{0} - {1}", DATA_TYPE_FOLDER_NAME, prevalue.Fieldsets.First().Label);
                                    datatype = dataTypeService.GetDataTypeDefinitionByName(datatypeName);
                                }
                            }
                        }

                        if (datatype != null)
                        {
                            //create new property type
                            var newProp = new PropertyType(datatype, alias);
                            newProp.Name = oldProp.Name;
                            newProp.Description = oldProp.Description;
                            newProp.Mandatory = oldProp.Mandatory;
                            newProp.ValidationRegExp = oldProp.ValidationRegExp;
                            newProp.SortOrder = oldProp.SortOrder - 1;

                            //add the new type to our content type
                            updatedContentType.AddPropertyType(newProp, tab);

                            //debug output
                            created[updatedContentType.Alias].Add(alias);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(typeof(MigrationController), e.Message, e);
                        errors[contentType.Alias].Add(alias);
                    }
                }

                //save the content type with new properties
                try
                {
                   contentTypeService.Save(updatedContentType);
                }
                catch (Exception e)
                {
                    Logger.Error(typeof(MigrationController), e.Message, e);
                }
            }
        }

        //filter out types that didn't have any properties converted
        var filteredCreated = created.Where(x => x.Value.Count() > 0);
        var filteredErrors = errors.Where(x => x.Value.Count() > 0);

        var json = new
        {
            errors = filteredErrors,
            created = filteredCreated
        };

        return Json(json, JsonRequestBehavior.AllowGet);
    }

    #endregion

    #region Step 8 - Copy data to new properties

    /// <summary>
    /// Iterate through the entire content tree and copy archetype data to nested content properties
    /// </summary>
    /// <returns></returns>
    public ActionResult CopyArchetypeData()
    {
        var roots = contentService.GetRootContent();
        foreach (var root in roots)
        {
            var descendents = contentService.GetDescendants(root);
            var pages = new List<IContent>() { root }.Concat(descendents);
            
            foreach (var page in pages)
            {
                Logger.Error(typeof(MigrationController), "Copying data for page: " + page.Id, null);
                foreach(var property in page.Properties.Where(x => x.PropertyType.PropertyEditorAlias == ARCHETYPE_PROPERTY_EDITOR))
                {
                    //if our property value is null, just skip
                    if (property.Value == null) continue;

                    //check if we can find a property without the suffix we added
                    var alias = property.Alias.Remove(property.Alias.IndexOf(PROPERTY_SUFFIX));
                    
                    var newProperty = page.Properties.FirstOrDefault(x => x.Alias == alias);
                    if (newProperty != null)
                    {
                        //now check that the data type is correct
                        var datatype = dataTypeService.GetDataTypeDefinitionById(newProperty.PropertyType.DataTypeDefinitionId);
                        var archetype = dataTypeService.GetDataTypeDefinitionById(property.PropertyType.DataTypeDefinitionId);
                        var datatypeName = string.Format("{0} - {1}", DATA_TYPE_FOLDER_NAME, archetype.Name);
                        
                        if(datatype.Name != datatypeName)
                        {
                            //try to get by fieldset name
                            //get the prevalue data which is where properties are defined
                            var prevalueJson = dataTypeService.GetPreValuesByDataTypeId(archetype.Id).First();
                            var prevalue = JsonConvert.DeserializeObject<ArchetypePreValue>(prevalueJson);
                            if(prevalue.Fieldsets.First() != null)
                            {
                                datatypeName = string.Format("{0} - {1}", DATA_TYPE_FOLDER_NAME, prevalue.Fieldsets.First().Label);
                            }
                        }
                        
                        if (datatype.Name == datatypeName)
                        {
                            string value = null;

                            //now we can copy data
                            try
                            {
                                var archetypeValue = JsonConvert.DeserializeObject<ArchetypeModel>(property.Value.ToString());

                                var blocks = GenerateNestedContent(archetypeValue, archetype.Name);
                                value = JsonConvert.SerializeObject(blocks);
                                page.SetValue(newProperty.Alias, value);

                                //save page
                                contentService.Save(page);
                            }
                            catch(Exception e)
                            {
                                Logger.Error(typeof(MigrationController), string.Format("Error copying data for page: {0}, property: {1}, value: {2}", page.Id, alias, value), e);
                            }
                        }
                    }
                }
            }
        }

        return Content("done");
    }

    /// <summary>
    /// Generates a nested content value from an Archetype model
    /// </summary>
    /// <param name="model"></param>
    /// <param name="archetypeName"></param>
    /// <returns></returns>
    private object GenerateNestedContent(ArchetypeModel model, string archetypeName)
    {
        //each fieldset should be a separate nested content
        var blocks = new List<Dictionary<string, object>>();
        foreach (var fieldset in model.Fieldsets)
        {
            //we might have a content type named after the data type, so check for that first 
            var contentType = contentTypeService.GetContentType(archetypeName.ToSafeAlias(true));
            if (contentType == null)
            {
                //find a content type with the fieldset alias
                contentType = contentTypeService.GetContentType(fieldset.Alias);
            }

            if(contentType == null)
            {
                //something is wrong if we get here
                Logger.Error(typeof(MigrationController), "Could not find content type for the archetype model with fieldset: " + fieldset.Alias, null);
                continue;
            }

            var block = new Dictionary<string, object>();
            block.Add("ncContentTypeAlias", contentType.Alias);
            block.Add("key", Guid.NewGuid());
            block.Add("umbracoNaviHide", fieldset.Disabled);

            foreach (var prop in fieldset.Properties)
            {
                //first check if this is an empty archetype object. If so, just set the prop to null
                if(prop.Value?.ToString() == "{\"fieldsets\":[]}")
                {
                    block.Add(prop.Alias, null);
                    continue;
                }

                //next check if this a nested archetype by trying to deserialize it into an ArchetypeModel
                ArchetypeModel nestedArchetype = null;
                try
                {
                    nestedArchetype = JsonConvert.DeserializeObject<ArchetypeModel>(prop.Value.ToString());
                }
                catch
                {
                    //nothing to see here, if we can't deserialize then we have a different property type
                }
                
                //check for fieldsets, because other datatypes (e.g., linkpicker) can be deserialized to an ArchetypeModel
                if(nestedArchetype != null && nestedArchetype.Fieldsets.Count() > 0)
                {
                    block.Add(prop.Alias, GenerateNestedContent(nestedArchetype, prop.Alias));
                }
                else
                {
                    //try to handle nupicker data
                    //we don't have access to the property editor for the achetype property, so find the "old" property on the target content type
                    var oldAlias = string.Format("{0}{1}", prop.Alias, PROPERTY_SUFFIX);
                    var propertyType = contentType.PropertyTypes.FirstOrDefault(x => x.Alias == oldAlias);

                    if(propertyType != null && prop.Value != null)
                    {
                        if (nupickerEditorMap.ContainsKey(propertyType.PropertyEditorAlias))
                        {
                            //first copy data to old field
                            block.Add(oldAlias, prop.Value?.ToString());

                            try
                            {
                                var newPropertyType = contentType.PropertyTypes.FirstOrDefault(x => x.Alias == prop.Alias);
                                var datatype = dataTypeService.GetDataTypeDefinitionById(newPropertyType.DataTypeDefinitionId);

                                if (datatype != null)
                                {
                                    //try to deserialize the value and get the dropdown/radiobutton key, if that fails use the raw value as our key for searching prevalues
                                    string key = "";
                                    try
                                    {
                                        var data = JsonConvert.DeserializeObject<List<EditorDataItem>>(prop.Value.ToString());
                                        key = data.FirstOrDefault().Key;
                                    }
                                    catch
                                    {
                                        key = prop.Value?.ToString() ?? "";
                                    }

                                    object value = "";

                                    if (datatype.PropertyEditorAlias == "Umbraco.RadioButtonList")
                                    {
                                        var prevalues = dataTypeService.GetPreValuesCollectionByDataTypeId(datatype.Id);
                                        //Umbraco 8+ needs the value not the Id
                                        value = prevalues.PreValuesAsDictionary.Where(x => x.Value.Value == key).FirstOrDefault().Value?.Value;
                                    }
                                    else if (datatype.PropertyEditorAlias == "Umbraco.DropDown.Flexible")
                                    {
                                        var prevalues = dataTypeService.GetPreValuesCollectionByDataTypeId(datatype.Id);
                                        //Umbraco 8+ needs the value not the Id, also wants the value in an array
                                        value = new List<string>() { prevalues.PreValuesAsDictionary.Where(x => x.Value.Value == key).FirstOrDefault().Value?.Value };
                                    }
                                    else if (datatype.PropertyEditorAlias == "Dawoe.OEmbedPickerPropertyEditor")
                                    {
                                        //create an OEmbed object
                                        value = ConvertOEmbed(prop.Value);
                                    }

                                    block.Add(prop.Alias, value);
                                }
                            }
                            catch (Exception e)
                            {
                                Logger.Error(typeof(MigrationController), string.Format("Error converting nupicker data for property: {0}", prop.Alias), e);
                                //just throw the old value in here for good measure
                                block.Add(prop.Alias, prop.Value?.ToString());
                            }
                        }
                        else if (propertyType.PropertyEditorAlias == LINKPICKER_PROPERTY_EDITOR)
                        {
                            //convert link picker to multi url picker by putting value in an array for storage
                            block.Add(prop.Alias, new[] { JsonConvert.DeserializeObject(prop.Value.ToString()) });
                        }
                    }
                    else
                    {
                        propertyType = contentType.PropertyTypes.FirstOrDefault(x => x.Alias == prop.Alias);

                        if (propertyType != null && prop.Value != null && udiConversions.Contains(propertyType.PropertyEditorAlias))
                        {
                            var value = ConvertToUdi(prop.Value);
                            block.Add(prop.Alias, value);
                        }
                        else if (propertyType.PropertyEditorAlias == "Dawoe.OEmbedPickerPropertyEditor")
                        {
                            //create an OEmbed object
                            block.Add(prop.Alias, ConvertOEmbed(prop.Value));
                        }
                        else
                        {
                            block.Add(prop.Alias, prop.Value?.ToString());
                        }
                    }
                }
            }

            blocks.Add(block);
        }

        return blocks;
    }

    #endregion

    #region Step 9 - Clean up old properties and datatypes

    public ActionResult RemoveArchetypesAndNupickers()
    {
        var datatypes = dataTypeService.GetAllDataTypeDefinitions();

        foreach(var datatype in datatypes)
        {
            if (datatype.PropertyEditorAlias == ARCHETYPE_PROPERTY_EDITOR || datatype.PropertyEditorAlias.StartsWith("nuPickers") || datatype.PropertyEditorAlias == LINKPICKER_PROPERTY_EDITOR)
            {
                dataTypeService.Delete(datatype);
            }
        }

        return Content("done");
    }

    #endregion

    #region Step 10 - Convert picker properties to UDI

    public ActionResult UpdatePickerAndVideoData()
    {
        var roots = contentService.GetRootContent();
        foreach (var root in roots)
        {
            var descendents = contentService.GetDescendants(root);
            var pages = new List<IContent>() { root }.Concat(descendents);

            foreach (var page in pages)
            {
                foreach (var property in page.Properties.Where(x => x.PropertyType.PropertyEditorAlias == "Umbraco.MediaPicker2"
                    || x.PropertyType.PropertyEditorAlias == "Umbraco.MultiNodeTreePicker2"))
                {
                    //if our property value is null, just skip
                    if (property.Value == null) continue;

                    try
                    {
                        property.Value = ConvertToUdi(property.Value);
                    }
                    catch(Exception e)
                    {
                        Logger.Error(typeof(MigrationController), string.Format("Error copying data for page: {0}, property: {1}, value: {2}", page.Id, property.Alias, property.Value), e);
                    }
                }

                //handle OEmbed fields
                foreach (var property in page.Properties.Where(x => x.PropertyType.PropertyEditorAlias == "Dawoe.OEmbedPickerPropertyEditor"))
                {
                    try
                    {
                        property.Value = ConvertOEmbed(property.Value);
                    }
                    catch (Exception e)
                    {
                        Logger.Error(typeof(MigrationController), string.Format("Error copying data for page: {0}, property: {1}, value: {2}", page.Id, property.Alias, property.Value), e);
                    }
                }

                contentService.Save(page);
            }
        }

        return Json(new { }, JsonRequestBehavior.AllowGet);
    }

    #endregion

    private string ConvertOEmbed(object value)
    {
        //return null if we have a null or empty string
        if (string.IsNullOrEmpty(value?.ToString())) return null;

        List<Dictionary<string, string>> newValue = new List<Dictionary<string, string>>(); //return value

        //deserialize value to determine if we're working with v7 oembed property or a nupicker property
        var list = JsonConvert.DeserializeObject<List<object>>(value.ToString());

        foreach(var valueObj in list)
        {
            if (valueObj is string valueStr)
            {
                var html = new HtmlDocument();
                html.LoadHtml(valueStr);
                var node = html.DocumentNode.SelectSingleNode("/iframe");

                if (node != null)
                {
                    newValue.Add(
                        new Dictionary<string, string>()
                        {
                            { "url", node.GetAttributeValue("src", "") },
                            { "width", node.GetAttributeValue("width", "") },
                            { "height", node.GetAttributeValue("height", "") },
                            { "preview", valueStr },
                        }
                    );
                }
            }
            else if(valueObj is JObject jobject)
            {
                if(jobject.ContainsKey("key"))
                {
                    var videoId = jobject["key"];
                    newValue.Add(
                        new Dictionary<string, string>()
                        {
                            { "url", string.Format("https://player.vimeo.com/video/{0}", videoId) },
                            { "width", "360" },
                            { "height", "203" },
                            { "preview", string.Format("<iframe src=\"https://player.vimeo.com/video/{0}\" width=\"360\" height=\"203\" frameborder=\"0\" allow=\"autoplay; fullscreen; picture-in-picture\" allowfullscreen title=\"{1}\"></iframe>", videoId, "") },
                        }
                    );
                }
            }
        }

        if(newValue.Any())
        {
            return JsonConvert.SerializeObject(newValue);
        }

        //if we get here, just return the original value
        return value.ToString();

    }

    /// <summary>
    /// Convert an int value to a UDI or list of UDIs for multiple picker fields and handle existing UDI values 
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private string ConvertToUdi(object value)
    {
        string retVal = value.ToString();

        var ids = retVal.SplitEasy(",");
        var udis = new List<string>();

        foreach (var idString in ids)
        {
            //if we have an int value, we need to config to a UDI
            if (int.TryParse(idString, out int id))
            {
                var reference = contentService.GetById(id);
                if (reference != null)
                {
                    udis.Add(reference.GetUdi().ToString());
                }
                else
                {
                    //try to get reference via the mediaservice
                    var media = mediaService.GetById(id);
                    if(media != null)
                    {
                        udis.Add(media.GetUdi().ToString());
                    }
                }
            }
            else if (idString.StartsWith("umb:"))
            {
                udis.Add(idString);
            }
        }

        retVal = string.Join(",", udis);

        return retVal;
    }

    /// <summary>
    /// Gets or creates a root-level datatype folder for the given name
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    private int GetOrCreateDataTypeContainer(string name)
    {
        var folders = dataTypeService.GetContainers(name, 1);
        if (folders.IsNullOrEmpty())
        {
            //create the folder
            var result = dataTypeService.CreateContainer(-1, name);
            if (result.Success)
            {
                return result.Result.Entity.Id;
            }
            else
            {
                throw result.Exception;
            }
        }

        return folders.First().Id;
    }

    /// <summary>
    /// Gets or creates a root-level contenttype folder for the given name
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    private int GetOrCreateContentTypeContainer(string name)
    {
        var folders = contentTypeService.GetContentTypeContainers(name, 1);
        if (folders.IsNullOrEmpty())
        {
            //create the folder
            var result = contentTypeService.CreateContentTypeContainer(-1, name);
            if (result.Success)
            {
                return result.Result.Entity.Id;
            }
            else
            {
                throw result.Exception;
            }
        }

        return folders.First().Id;
    }

    /// <summary>
    /// Return the first string that is not null or whitespace
    /// </summary>
    /// <param name="strings"></param>
    /// <returns></returns>
    private string FirstWithValue(params string[] strings)
    {
        return strings.FirstOrDefault(x => !x.IsNullOrWhiteSpace()) ?? "";
    }

    //stolen from nupickers, used for getting data from json
    private static string GetDataFromUrl(string url)
    {
        string empty = string.Empty;
        if (!string.IsNullOrEmpty(url))
        {
            if (VirtualPathUtility.IsAppRelative(url))
            {
                bool flag = false;
                if (!url.Contains("?"))
                {
                    string text = System.Web.HttpContext.Current.Server.MapPath(url);
                    if (System.IO.File.Exists(text))
                    {
                        url = text;
                        flag = true;
                    }
                }
                if (!flag)
                {
                    url = url.Replace("~/", System.Web.HttpContext.Current.Request.Url.GetLeftPart(UriPartial.Authority) + "/");
                }
            }
            WebClient webClient = new WebClient();
            return webClient.DownloadString(url);
        }
        return empty;
    }


    private static Assembly GetAssembly(string assemblyName)
    {
        if (string.Equals(assemblyName, "App_Code", StringComparison.InvariantCultureIgnoreCase))
        {
            try
            {
                return Assembly.Load(assemblyName);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
        string text = HostingEnvironment.MapPath("~/bin/" + assemblyName);
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                return Assembly.Load(System.IO.File.ReadAllBytes(text));
            }
            catch
            {
                return null;
            }
        }
        return null;
    }
}