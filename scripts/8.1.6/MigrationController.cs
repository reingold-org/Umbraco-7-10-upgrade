using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Umbraco.Core.Models;
using Umbraco.Core.Services;
using Umbraco.Web.Mvc;

public class MigrationController : SurfaceController
{
    private readonly IContentTypeService contentTypeService;
    private readonly IContentService contentService;

    public MigrationController(IContentTypeService contentTypeService, IContentService contentService)
    {
        this.contentTypeService = contentTypeService;
        this.contentService = contentService;
    }

    public ActionResult SetElementTypes()
    {
        var containers = new List<string> { "Blocks", "Nested Doc types" };

        foreach(var container in containers)
        {
            var containerId = GetContentTypeContainer(container);
            ConvertChildrenToElements(containerId);
        }

        //get subfolders by ids because the stupid api wont let me get children of containers
        /*
        var subFolders = contentTypeService.GetContainers(new int[] { 6340, 6346, 6851 });
        
        foreach (var container in subFolders)
        {
            ConvertChildrenToElements(container.Id);
        }
        */
        return Content("done");
    }

    private void ConvertChildrenToElements(int containerId)
    {
        var doctypes = contentTypeService.GetChildren(containerId);

        foreach (var doctype in doctypes)
        {
            doctype.IsElement = true;
        }
        contentTypeService.Save(doctypes);
    }

    private int GetContentTypeContainer(string name)
    {
        var folders = contentTypeService.GetContainers(name, 1);
        return folders.First().Id;
    }
}