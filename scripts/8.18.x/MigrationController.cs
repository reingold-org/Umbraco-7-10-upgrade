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


    public ActionResult PublishContent()
    {
        var pages = new int[] {  }
            .OrderBy(x => x);
        var errors = new List<int>();

        foreach(var id in pages)
        {
            errors = errors.Concat(PublishPage(id)).ToList();
        }

        return Json(errors, JsonRequestBehavior.AllowGet);
    }

    private List<int> PublishPage(int pageId)
    {
        List<int> errors = new List<int>();

        var page = contentService.GetById(pageId);

        if (page != null)
        {
            //check if parent page is published first
            //errors = PublishPage(page.ParentId);

            try
            {
                var result = contentService.SaveAndPublish(page, raiseEvents: false);
            }
            catch
            {
                errors.Add(pageId);
            }
        }

        return errors;
    }
}