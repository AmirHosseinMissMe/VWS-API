﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using vws.web.Domain;
using vws.web.Models;
using vws.web.Repositories;

namespace vws.web.Controllers._file
{
    [Route("{culture:culture}/[controller]")]
    [ApiController]
    public class FileController : BaseController
    {
        private readonly IConfiguration configuration;
        private readonly IStringLocalizer<FileController> localizer;
        private readonly IVWS_DbContext vwsDbContext;
        private readonly IFileManager fileManager;

        public FileController(IConfiguration _configuration, IStringLocalizer<FileController> _localizer,
            IVWS_DbContext _vwsDbContext, IFileManager _fileManager)
        {
            configuration = _configuration;
            localizer = _localizer;
            vwsDbContext = _vwsDbContext;
            fileManager = _fileManager;
        }

        [HttpPost]
        [Authorize]
        [RequestFormLimits(MultipartBodyLengthLimit = 209715200)]
        [RequestSizeLimit(209715200)]
        [Route("upload")]
        public async Task<IActionResult> UploadFiles(List<IFormFile> formFiles)
        {
            var files = Request.Form.Files.Union(formFiles);
            var response = new ResponseModel();
            List<Domain._file.File> successfullyUploadedFiles = new List<Domain._file.File>();
            List<Domain._file.FileContainer> successfullyUploadedFileContainers = new List<Domain._file.FileContainer>();
            bool hasError = false;
            var time = DateTime.Now;
            foreach (var file in files)
            {
                var newFileContainer = new Domain._file.FileContainer()
                {
                    ModifiedBy = LoggedInUserId.Value,
                    CreatedBy = LoggedInUserId.Value,
                    CreatedOn = time,
                    ModifiedOn = time,
                    Guid = Guid.NewGuid()
                };
                await vwsDbContext.AddFileContainerAsync(newFileContainer);
                vwsDbContext.Save();
                successfullyUploadedFileContainers.Add(newFileContainer);
                var result = await fileManager.WriteFile(file, LoggedInUserId.Value, "files", newFileContainer.Id);
                if (result.HasError)
                {
                    foreach (var error in result.Errors)
                        response.AddError(localizer[error]);
                    hasError = true;
                    break;
                }
                else
                {
                    successfullyUploadedFiles.Add(result.Value);
                    newFileContainer.RecentFileId = result.Value.Id;
                    vwsDbContext.Save();
                }
            }
            vwsDbContext.Save();
            if (hasError)
            {
                foreach (var successFile in successfullyUploadedFiles)
                {
                    fileManager.DeleteFile(successFile.Address);
                    vwsDbContext.DeleteFile(successFile);
                }
                foreach (var successfileContainer in successfullyUploadedFileContainers)
                {
                    vwsDbContext.DeleteFileContainer(successfileContainer);
                }
                vwsDbContext.Save();
                response.Message = "Unsuccessful writing";
                return StatusCode(StatusCodes.Status500InternalServerError, response);
            }
            response.Message = "Successful writing";
            return Ok(response);
        }


        [HttpGet]
        [Route("get")]
        public async Task<IActionResult> GetFile(int id)
        {
            var response = new ResponseModel();
            var fileContainer = await vwsDbContext.GetFileContainerAsync(id);
            if (fileContainer == null)
            {
                response.AddError(localizer["There is no such file."]);
                response.Message = "File not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }
            var selectedFile = (await vwsDbContext.GetFileAsync(fileContainer.RecentFileId));
            string address = selectedFile.Address;
            string fileName = selectedFile.Name;


            var memory = new MemoryStream();
            using (var stream = new FileStream(address, FileMode.Open))
            {
                await stream.CopyToAsync(memory);
            }
            memory.Position = 0;
            return File(memory, "application/" + selectedFile.Extension, Path.GetFileName(fileName));
        }
    }
}
