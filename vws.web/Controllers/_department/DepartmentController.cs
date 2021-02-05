﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using vws.web.Domain;
using vws.web.Domain._base;
using vws.web.Models;
using vws.web.Repositories;
using Microsoft.EntityFrameworkCore;
using vws.web.Models._department;
using vws.web.Domain._department;
using System.Collections.Generic;

namespace vws.web.Controllers._department
{
    [Route("{culture:culture}/[controller]")]
    [ApiController]
    public class DepartmentController : BaseController
    {
        private readonly IStringLocalizer<DepartmentController> localizer;
        private readonly IVWS_DbContext vwsDbContext;
        private readonly IConfiguration configuration;

        public DepartmentController(IConfiguration _configuration,
            IStringLocalizer<DepartmentController> _localizer, IVWS_DbContext _vwsDbContext)
        {
            localizer = _localizer;
            vwsDbContext = _vwsDbContext;
            configuration = _configuration;
        }

        [HttpPost]
        [Authorize]
        [Route("create")]
        public async Task<IActionResult> CreateDepartment([FromBody] DepartmentModel model)
        {
            var response = new ResponseModel<DepartmentResponseModel>();

            if (!String.IsNullOrEmpty(model.Description) && model.Description.Length > 2000)
            {
                response.Message = "Department model data has problem.";
                response.AddError(localizer["Length of description is more than 2000 characters."]);
            }
            if (model.Name.Length > 500)
            {
                response.Message = "Department model data has problem.";
                response.AddError(localizer["Length of name is more than 500 characters."]);
            }
            if (!String.IsNullOrEmpty(model.Color) && model.Color.Length > 6)
            {
                response.Message = "Department model data has problem.";
                response.AddError(localizer["Length of color is more than 6 characters."]);
            }
            if (response.HasError)
                return StatusCode(StatusCodes.Status500InternalServerError, response);

            var selectedTeam = await vwsDbContext.GetTeamAsync(model.TeamId);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.AddError(localizer["There is no team with such id."]);
                response.Message = "Team not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var userId = LoggedInUserId.Value;

            var selectedTeamMember = await vwsDbContext.GetTeamMemberAsync(model.TeamId, userId);
            if (selectedTeamMember == null)
            {
                response.AddError(localizer["You are not member of team."]);
                response.Message = "Team access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            var departmentNames = vwsDbContext.Departments.Where(department => department.TeamId == model.TeamId && department.IsDeleted == false).Select(department => department.Name);

            if (departmentNames.Contains(model.Name))
            {
                response.AddError(localizer["There is already a department with given name, in given team."]);
                response.Message = "Name of department is used";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var time = DateTime.Now;

            var newDepartment = new Department()
            {
                Name = model.Name,
                Description = model.Description,
                IsDeleted = false,
                Color = model.Color,
                TeamId = model.TeamId,
                Guid = Guid.NewGuid(),
                CreatedBy = userId,
                ModifiedBy = userId,
                CreatedOn = time,
                ModifiedOn = time
            };

            await vwsDbContext.AddDepartmentAsync(newDepartment);
            vwsDbContext.Save();

            var newDepartmentMember = new DepartmentMember()
            {
                IsDeleted = false,
                CreatedOn = time,
                UserProfileId = userId,
                DepartmentId = newDepartment.Id
            };

            await vwsDbContext.AddDepartmentMemberAsync(newDepartmentMember);
            vwsDbContext.Save();

            var departmentResponse = new DepartmentResponseModel()
            {
                Id = newDepartment.Id,
                Color = newDepartment.Color,
                Name = newDepartment.Name,
                TeamId = newDepartment.TeamId
            };

            response.Value = departmentResponse;
            response.Message = "Department added successfully";
            return Ok(response);
        }


        [HttpPut]
        [Authorize]
        [Route("update")]
        public async Task<IActionResult> UpdateDepartment([FromBody] UpdateDepartmentModel model)
        {
            var response = new ResponseModel<DepartmentResponseModel>();

            if (!String.IsNullOrEmpty(model.Description) && model.Description.Length > 2000)
            {
                response.Message = "Department model data has problem.";
                response.AddError(localizer["Length of description is more than 2000 characters."]);
            }
            if (model.Name.Length > 500)
            {
                response.Message = "Department model data has problem.";
                response.AddError(localizer["Length of name is more than 500 characters."]);
            }
            if (!String.IsNullOrEmpty(model.Color) && model.Color.Length > 6)
            {
                response.Message = "Department model data has problem.";
                response.AddError(localizer["Length of color is more than 6 characters."]);
            }
            if (response.HasError)
                return StatusCode(StatusCodes.Status500InternalServerError, response);

            var selectedTeam = await vwsDbContext.GetTeamAsync(model.TeamId);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.AddError(localizer["There is no team with such id."]);
                response.Message = "Team not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var userId = LoggedInUserId.Value;

            var selectedTeamMember = await vwsDbContext.GetTeamMemberAsync(model.TeamId, userId);
            if (selectedTeamMember == null)
            {
                response.AddError(localizer["You are not member of team."]);
                response.Message = "Team access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            var selectedDepartment = vwsDbContext.Departments.FirstOrDefault(department => department.Id == model.Id);
            if (selectedDepartment == null || selectedDepartment.IsDeleted)
            {
                response.AddError(localizer["There is no department with such id."]);
                response.Message = "Department not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!vwsDbContext.DepartmentMembers.Any(departmentMember => departmentMember.Id == model.Id &&
                                                   departmentMember.UserProfileId == userId && departmentMember.IsDeleted == false))
            {
                response.AddError(localizer["You are not member of given department."]);
                response.Message = "Department access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            if (selectedDepartment.TeamId != model.TeamId)
            {
                var departmentNames = vwsDbContext.Departments.Where(department => department.TeamId == model.TeamId && department.IsDeleted == false).Select(department => department.Name);

                if (departmentNames.Contains(model.Name))
                {
                    response.AddError(localizer["There is already a department with given name, in given team."]);
                    response.Message = "Name of department is used";
                    return StatusCode(StatusCodes.Status400BadRequest, response);
                }
            }

            selectedDepartment.TeamId = model.TeamId;
            selectedDepartment.Name = model.Name;
            selectedDepartment.ModifiedBy = userId;
            selectedDepartment.ModifiedOn = DateTime.Now;
            selectedDepartment.Color = model.Color;
            selectedDepartment.Description = model.Description;

            vwsDbContext.Save();

            var departmentResponse = new DepartmentResponseModel()
            {
                Color = selectedDepartment.Color,
                Id = selectedDepartment.Id,
                Name = selectedDepartment.Name,
                TeamId = selectedDepartment.TeamId
            };

            response.Message = "Department updated successfully!";
            response.Value = departmentResponse;
            return Ok(response);
        }

        [HttpDelete]
        [Authorize]
        [Route("delete")]
        public IActionResult DeleteDepartment(int id)
        {
            var response = new ResponseModel();

            var userId = LoggedInUserId.Value;

            var selectedDepartment = vwsDbContext.Departments.FirstOrDefault(department => department.Id == id);

            if (selectedDepartment == null || selectedDepartment.IsDeleted)
            {
                response.AddError(localizer["There is no department with such id."]);
                response.Message = "Department not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!vwsDbContext.DepartmentMembers.Any(departmentMember => departmentMember.Id == id &&
                                                   departmentMember.UserProfileId == userId && departmentMember.IsDeleted == false))
            {
                response.AddError(localizer["You are not member of given department."]);
                response.Message = "Department access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            selectedDepartment.IsDeleted = true;
            selectedDepartment.ModifiedBy = userId;
            selectedDepartment.ModifiedOn = DateTime.Now;

            vwsDbContext.Save();

            response.Message = "Department deleted successfully!";
            return Ok(response);
        }

        [HttpGet]
        [Authorize]
        [Route("get")]
        public IEnumerable<DepartmentResponseModel> GetDepartment()
        {
            List<DepartmentResponseModel> response = new List<DepartmentResponseModel>();

            var userId = LoggedInUserId.Value;

            var userDepartments = vwsDbContext.GetUserDepartments(userId).ToList();

            foreach (var userDepartment in userDepartments)
            {
                response.Add(new DepartmentResponseModel()
                {
                    Color = userDepartment.Color,
                    Id = userDepartment.Id,
                    Name = userDepartment.Name,
                    TeamId = userDepartment.TeamId
                });
            }

            return response;
        }

        [HttpPost]
        [Authorize]
        [Route("addTeammateToDepartment")]
        public async Task<IActionResult> AddTeamMateToDepartment([FromBody] AddTeammateToDepartmentModel model)
        {
            var response = new ResponseModel();
            var userId = LoggedInUserId.Value;
            var selectedUserId = new Guid(model.UserId);

            var selectedDepartment = vwsDbContext.Departments.FirstOrDefault(department => department.Id == model.DepartmentId);

            if (selectedDepartment == null || selectedDepartment.IsDeleted)
            {
                response.AddError(localizer["There is no department with such id."]);
                response.Message = "Department not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var selectedTeam = await vwsDbContext.GetTeamAsync(model.TeamId);

            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.AddError(localizer["There is no team with given Id."]);
                response.Message = "Team not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!vwsDbContext.UserProfiles.Any(profile => profile.UserId == selectedUserId))
            {
                response.AddError(localizer["There is no user with given Id."]);
                response.Message = "User not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var selectedTeamMember = await vwsDbContext.GetTeamMemberAsync(model.TeamId, userId);
            if(selectedTeamMember == null)
            {
                response.AddError(localizer["You are not member of team."]);
                response.Message = "Not member of team";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            selectedTeamMember = await vwsDbContext.GetTeamMemberAsync(model.TeamId, selectedUserId);
            if(selectedTeamMember == null)
            {
                response.AddError(localizer["User you want to to add, is not a member of selected team."]);
                response.Message = "Not member of team";
                return StatusCode(StatusCodes.Status406NotAcceptable, response);
            }

            if(!vwsDbContext.DepartmentMembers.Any(departmentMember => departmentMember.UserProfileId == userId && departmentMember.DepartmentId == model.DepartmentId && !departmentMember.IsDeleted))
            {
                response.AddError(localizer["You are not member of given department."]);
                response.Message = "Department access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            if (vwsDbContext.DepartmentMembers.Any(departmentMember => departmentMember.UserProfileId == selectedUserId && departmentMember.DepartmentId == model.DepartmentId && !departmentMember.IsDeleted))
            {
                response.AddError(localizer["User you want to add, is already a member of selected department."]);
                response.Message = "User added before";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var newDepartmentMember = new DepartmentMember()
            {
                CreatedOn = DateTime.Now,
                IsDeleted = false,
                DepartmentId = model.DepartmentId,
                UserProfileId = selectedUserId
            };

            await vwsDbContext.AddDepartmentMemberAsync(newDepartmentMember);
            vwsDbContext.Save();

            response.Message = "User added to department successfully!";
            return Ok(response);
        }
    }
}
