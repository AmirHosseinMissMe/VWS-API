﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using vws.web.Domain;
using vws.web.Domain._base;
using vws.web.Domain._file;
using vws.web.Domain._project;
using vws.web.Domain._team;
using vws.web.Enums;
using vws.web.Models;
using vws.web.Models._project;
using vws.web.Repositories;
using vws.web.Services._chat;

namespace vws.web.Controllers._project
{
    [Route("{culture:culture}/[controller]")]
    [ApiController]
    public class ProjectController : BaseController
    {
        private readonly UserManager<ApplicationUser> userManager;
        private readonly IStringLocalizer<ProjectController> localizer;
        private readonly IVWS_DbContext vwsDbContext;
        private readonly IFileManager fileManager;
        private readonly IChannelService channelService;

        public ProjectController(UserManager<ApplicationUser> _userManager, IStringLocalizer<ProjectController> _localizer,
            IVWS_DbContext _vwsDbContext, IFileManager _fileManager, IChannelService _channelService)
        {
            userManager = _userManager;
            localizer = _localizer;
            vwsDbContext = _vwsDbContext;
            fileManager = _fileManager;
            channelService = _channelService;
        }

        private List<string> CheckProjectModel(ProjectModel model)
        {
            var result = new List<string>();

            if (!String.IsNullOrEmpty(model.Description) && model.Description.Length > 2000)
                result.Add(localizer["Length of description is more than 2000 characters."]);

            if (model.Name.Length > 500)
                result.Add(localizer["Length of title is more than 500 characters."]);

            if (!String.IsNullOrEmpty(model.Color) && model.Color.Length > 6)
                result.Add(localizer["Length of color is more than 6 characters."]);

            if (model.StartDate.HasValue && model.EndDate.HasValue && model.StartDate > model.EndDate)
                result.Add(localizer["Start Date should be before End Date."]);

            return result;
        }

        private List<string> CheckAreDepartmentsForTeam(int teamId, List<int> departmentIds)
        {
            var result = new List<string>();

            foreach (var departmentId in departmentIds)
            {
                var selectedDepartment = vwsDbContext.Departments.FirstOrDefault(department => department.Id == departmentId);

                if (selectedDepartment.TeamId != teamId)
                    result.Add(String.Format(localizer["Department with name {0} is not for selected team."], selectedDepartment.Name));
            }

            return result;
        }

        private List<string> CheckDepartmentExistency(List<int> departmentIds)
        {
            var result = new List<string>();

            foreach (var departmentId in departmentIds)
            {
                var selectedDepartment = vwsDbContext.Departments.FirstOrDefault(department => department.Id == departmentId);
                if (selectedDepartment == null || selectedDepartment.IsDeleted)
                    result.Add(String.Format(localizer["There is no department with id {0}."], departmentId));
            }

            return result;
        }

        private List<string> CheckBegingAMemberOfDepartment(Guid userId, List<int> departmentIds)
        {
            var result = new List<string>();

            foreach (var departmentId in departmentIds)
            {
                var selectedDepartmentMember = vwsDbContext.DepartmentMembers.FirstOrDefault(departmentMember => departmentMember.UserProfileId == userId &&
                                                                                                                 departmentMember.DepartmentId == departmentId &&
                                                                                                                 departmentMember.IsDeleted == false);

                var selectedDepartment = vwsDbContext.Departments.FirstOrDefault(department => department.Id == departmentId);

                if (selectedDepartmentMember == null)
                    result.Add(String.Format(localizer["You are not member of department with name {0}."], selectedDepartment.Name));
            }

            return result;
        }

        private List<Guid> GetAvailableUsersToAddProject(Project project, Guid userId)
        {
            var availableUsers = new List<Guid>();

            var projectUsers = vwsDbContext.ProjectMembers.Where(projectMember => projectMember.ProjectId == project.Id &&
                                                                                  !projectMember.IsDeleted)
                                                          .Select(projectMember => projectMember.UserProfileId);

            List<Team> userTeams = vwsDbContext.GetUserTeams(userId).ToList();
            List<Guid> userTeamMates = vwsDbContext.TeamMembers
                .Where(teamMember => userTeams.Select(userTeam => userTeam.Id).Contains(teamMember.TeamId) && !teamMember.IsDeleted)
                .Select(teamMember => teamMember.UserProfileId).Distinct().ToList();

            availableUsers = userTeamMates.Except(projectUsers).ToList();

            return availableUsers;
        }

        private bool HasAccessToProject(Guid userId, int projectId)
        {
            var selectedProject = vwsDbContext.Projects.Include(project => project.ProjectDepartments)
                                                       .FirstOrDefault(project => project.Id == projectId);

            if (selectedProject.TeamId != null)
            {
                List<Guid> projectUsers = new List<Guid>();

                if (selectedProject.ProjectDepartments.Count == 0)
                {
                    projectUsers = vwsDbContext.TeamMembers.Where(teamMember => teamMember.TeamId == (int)selectedProject.TeamId &&
                                                                                                     !teamMember.IsDeleted)
                                                           .Select(teamMember => teamMember.UserProfileId)
                                                           .ToList();
                }

                else
                {
                    foreach (var departmentId in selectedProject.ProjectDepartments.Select(pd => pd.DepartmentId))
                    {
                        projectUsers.AddRange(vwsDbContext.DepartmentMembers.Where(departmentMember => departmentMember.DepartmentId == departmentId &&
                                                                                                       !departmentMember.IsDeleted)
                                                                            .Select(departmentMember => departmentMember.UserProfileId)
                                                                            .ToList());
                    }
                }

                return projectUsers.Contains(userId);
            }

            var selectedProjectMember = vwsDbContext.ProjectMembers.FirstOrDefault(projectMember => !projectMember.IsDeleted &&
                                                                                                    projectMember.IsPermittedByCreator == true &&
                                                                                                    projectMember.ProjectId == projectId &&
                                                                                                    projectMember.UserProfileId == userId);

            return selectedProjectMember != null;
        }

        private async Task<List<UserModel>> GetProjectUsers(int projectId)
        {
            var selectedProject = vwsDbContext.Projects.Include(project => project.ProjectDepartments)
                                                       .FirstOrDefault(project => project.Id == projectId);

            List<UserProfile> projectUsers = new List<UserProfile>();

            if (selectedProject.TeamId != null)
            {
                if (selectedProject.ProjectDepartments.Count == 0)
                {
                    projectUsers = vwsDbContext.TeamMembers.Include(teamMember => teamMember.UserProfile)
                                                           .Where(teamMember => teamMember.TeamId == (int)selectedProject.TeamId &&
                                                                                                     !teamMember.IsDeleted)
                                                           .Select(teamMember => teamMember.UserProfile)
                                                           .ToList();
                }

                else
                {
                    foreach (var departmentId in selectedProject.ProjectDepartments.Select(pd => pd.DepartmentId))
                    {
                        projectUsers.AddRange(vwsDbContext.DepartmentMembers.Include(departmentMember => departmentMember.UserProfile)
                                                                            .Where(departmentMember => departmentMember.DepartmentId == departmentId &&
                                                                                                       !departmentMember.IsDeleted)
                                                                            .Select(departmentMember => departmentMember.UserProfile)
                                                                            .ToList());
                    }
                }
            }

            else
            {
                projectUsers = vwsDbContext.ProjectMembers.Include(projectMember => projectMember.UserProfile)
                                                          .Where(projectMember => !projectMember.IsDeleted &&
                                                                                  projectMember.IsPermittedByCreator == true &&
                                                                                  projectMember.ProjectId == projectId)
                                                          .Select(projectMember => projectMember.UserProfile)
                                                          .ToList();
            }

            List<UserModel> users = new List<UserModel>();

            foreach (var user in projectUsers)
            {
                users.Add(new UserModel()
                {
                    UserId = user.UserId,
                    ProfileImageId = user.ProfileImageId,
                    UserName = (await userManager.FindByIdAsync(user.UserId.ToString())).UserName
                });
            }

            return users;
        }

        private List<Project> GetAllUserProjects(Guid userId)
        {
            List<Project> userProjects = new List<Project>();

            List<int> userTeams = vwsDbContext.GetUserTeams(userId).Select(team => team.Id).ToList();
            List<int> userDepartments = vwsDbContext.DepartmentMembers.Include(departmentMember => departmentMember.Department)
                                                                      .Where(departmentMember => !departmentMember.IsDeleted &&
                                                                                                 departmentMember.UserProfileId == userId &&
                                                                                                 !departmentMember.Department.IsDeleted)
                                                                      .Select(departmentMember => departmentMember.DepartmentId)
                                                                      .ToList();

            userProjects.AddRange(vwsDbContext.Projects.Include(project => project.ProjectDepartments)
                                                       .Where(project => project.IsDeleted == false &&
                                                                         project.TeamId != null &&
                                                                         project.ProjectDepartments.Count == 0 &&
                                                                         userTeams.Contains((int)project.TeamId))
                                                       .ToList());

            foreach (var project in vwsDbContext.Projects.Include(project => project.ProjectDepartments))
            {
                if (project.IsDeleted == false && project.TeamId != null &&
                   project.ProjectDepartments.Count != 0 &&
                   userTeams.Contains((int)project.TeamId) &&
                   project.ProjectDepartments.Select(pd => pd.DepartmentId).Intersect(userDepartments).Any())
                {
                    userProjects.Add(project);
                }
            }

            userProjects.AddRange(vwsDbContext.ProjectMembers.Include(projectMember => projectMember.Project)
                                                             .Where(projectMember => projectMember.UserProfileId == userId &&
                                                                                     !projectMember.IsDeleted &&
                                                                                     projectMember.IsPermittedByCreator == true &&
                                                                                     !projectMember.Project.IsDeleted)
                                                             .Select(projectMember => projectMember.Project));

            return userProjects;
        }

        [HttpPost]
        [Authorize]
        [Route("create")]
        public async Task<IActionResult> CreateProject([FromBody] ProjectModel model)
        {
            var response = new ResponseModel<ProjectResponseModel>();

            var checkModelResult = CheckProjectModel(model);

            if (checkModelResult.Count != 0)
            {
                response.Message = "Invalid project model";
                response.AddErrors(checkModelResult);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            Guid userId = LoggedInUserId.Value;

            if (model.TeamId != null)
            {
                var selectedTeam = await vwsDbContext.GetTeamAsync((int)model.TeamId);
                if (selectedTeam == null || selectedTeam.IsDeleted)
                {
                    response.AddError(localizer["There is no team with given Id."]);
                    response.Message = "Team not found";
                    return StatusCode(StatusCodes.Status400BadRequest, response);
                }
                var selectedTeamMember = await vwsDbContext.GetTeamMemberAsync((int)model.TeamId, userId);
                if (selectedTeamMember == null)
                {
                    response.AddError(localizer["You are not a member of team."]);
                    response.Message = "Not member of team";
                    return StatusCode(StatusCodes.Status403Forbidden, response);
                }

                var checkDepartmentExistencytResult = CheckDepartmentExistency(model.DepartmentIds);

                if (checkDepartmentExistencytResult.Count != 0)
                {
                    response.AddErrors(checkDepartmentExistencytResult);
                    response.Message = "Department not found";
                    return StatusCode(StatusCodes.Status400BadRequest, response);
                }

                var checkBeingMemberOfDepartmentsResult = CheckBegingAMemberOfDepartment(userId, model.DepartmentIds);

                if (checkBeingMemberOfDepartmentsResult.Count != 0)
                {
                    response.AddErrors(checkBeingMemberOfDepartmentsResult);
                    response.Message = "Department access denied";
                    return StatusCode(StatusCodes.Status403Forbidden, response);
                }

                var checkDepartmentAreForTeamResult = CheckAreDepartmentsForTeam((int)model.TeamId, model.DepartmentIds);

                if (checkDepartmentAreForTeamResult.Count != 0)
                {
                    response.AddErrors(checkDepartmentAreForTeamResult);
                    response.Message = "Department not for team";
                    return StatusCode(StatusCodes.Status400BadRequest, response);
                }
            }
            else if (model.DepartmentIds.Count != 0)
            {
                response.AddError(localizer["If your project is under department, you should specify the team."]);
                response.Message = "Invalid projectmodel";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            DateTime creationTime = DateTime.Now;

            var newProject = new Project()
            {
                Name = model.Name,
                StatusId = (byte)SeedDataEnum.ProjectStatuses.Active,
                Description = model.Description,
                Color = model.Color,
                StartDate = model.StartDate,
                EndDate = model.EndDate,
                Guid = Guid.NewGuid(),
                IsDeleted = false,
                CreateBy = userId,
                ModifiedBy = userId,
                CreatedOn = creationTime,
                ModifiedOn = creationTime,
                TeamId = model.TeamId
            };

            await vwsDbContext.AddProjectAsync(newProject);
            vwsDbContext.Save();

            var newProjectHistory = new ProjectHistory()
            {
                ProjectId = newProject.Id,
                Event = "Project created by {0}.",
                CommaSepratedParameters = (await userManager.FindByIdAsync(userId.ToString())).UserName,
                EventTime = creationTime
            };

            vwsDbContext.AddProjectHistory(newProjectHistory);
            vwsDbContext.Save();

            foreach (var departmentId in model.DepartmentIds)
                vwsDbContext.AddProjectDepartment(new ProjectDepartment { DepartmentId = departmentId, ProjectId = newProject.Id });

            if (newProject.TeamId == null)
            {
                var newProjectMember = new ProjectMember()
                {
                    CreatedOn = creationTime,
                    ProjectId = newProject.Id,
                    UserProfileId = userId,
                    IsDeleted = false,
                    IsPermittedByCreator = true,
                    PermittedOn = creationTime
                };
                await vwsDbContext.AddProjectMemberAsync(newProjectMember);
                vwsDbContext.Save();
            }

            var newProjectResponse = new ProjectResponseModel()
            {
                Id = newProject.Id,
                StatusId = newProject.StatusId,
                Name = newProject.Name,
                Description = newProject.Description,
                Color = newProject.Color,
                StartDate = newProject.StartDate,
                EndDate = newProject.EndDate,
                Guid = newProject.Guid,
                IsDelete = newProject.IsDeleted,
                TeamId = newProject.TeamId,
                ProjectImageId = newProject.ProjectImageId,
                DepartmentIds = model.DepartmentIds,
                NumberOfUpdates = vwsDbContext.ProjectHistories.Where(history => history.ProjectId == newProject.Id).Count(),
                Users = await GetProjectUsers(newProject.Id)
            };

            response.Value = newProjectResponse;
            response.Message = "Project created successfully!";
            return Ok(response);
        }

        [HttpPut]
        [Authorize]
        [Route("update")]
        public async Task<IActionResult> UpdateProject([FromBody] UpdateProjectModel model)
        {
            var response = new ResponseModel<ProjectResponseModel>();

            var checkModelResult = CheckProjectModel(model);

            if (checkModelResult.Count != 0)
            {
                response.Message = "Invalid project model";
                response.AddErrors(checkModelResult);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            Guid userId = LoggedInUserId.Value;

            if (model.TeamId != null)
            {
                var selectedTeam = await vwsDbContext.GetTeamAsync((int)model.TeamId);
                if (selectedTeam == null || selectedTeam.IsDeleted)
                {
                    response.AddError(localizer["There is no team with given Id."]);
                    response.Message = "Team not found";
                    return StatusCode(StatusCodes.Status400BadRequest, response);
                }
                var selectedTeamMember = await vwsDbContext.GetTeamMemberAsync((int)model.TeamId, userId);
                if (selectedTeamMember == null)
                {
                    response.AddError(localizer["You are not a member of team."]);
                    response.Message = "Not member of team";
                    return StatusCode(StatusCodes.Status403Forbidden, response);
                }

                var checkDepartmentExistencytResult = CheckDepartmentExistency(model.DepartmentIds);

                if (checkDepartmentExistencytResult.Count != 0)
                {
                    response.AddErrors(checkDepartmentExistencytResult);
                    response.Message = "Department not found";
                    return StatusCode(StatusCodes.Status400BadRequest, response);
                }

                var checkBeingMemberOfDepartmentsResult = CheckBegingAMemberOfDepartment(userId, model.DepartmentIds);

                if (checkBeingMemberOfDepartmentsResult.Count != 0)
                {
                    response.AddErrors(checkBeingMemberOfDepartmentsResult);
                    response.Message = "Department access denied";
                    return StatusCode(StatusCodes.Status403Forbidden, response);
                }

                var checkDepartmentAreForTeamResult = CheckAreDepartmentsForTeam((int)model.TeamId, model.DepartmentIds);

                if (checkDepartmentAreForTeamResult.Count != 0)
                {
                    response.AddErrors(checkDepartmentAreForTeamResult);
                    response.Message = "Department not for team";
                    return StatusCode(StatusCodes.Status400BadRequest, response);
                }
            }
            else if (model.DepartmentIds.Count != 0)
            {
                response.AddError(localizer["If your project is under department, you should specify the team."]);
                response.Message = "Invalid projectmodel";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var selectedProject = vwsDbContext.Projects.Include(project => project.ProjectDepartments).FirstOrDefault(project => project.Id == model.Id);
            if (selectedProject == null || selectedProject.IsDeleted)
            {
                response.Message = "Project not found";
                response.AddError(localizer["There is no project with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            bool isProjectPersonal = selectedProject.TeamId == null ? true : false;
            bool willBeProjectPersonal = model.TeamId == null ? true : false;

            if (!HasAccessToProject(userId, model.Id))
            {
                response.Message = "Project access denied";
                response.AddError(localizer["You are not a memeber of project."]);
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            if (isProjectPersonal && userId != selectedProject.CreateBy)
            {
                response.AddError(localizer["Updating personal project just can be done by creator."]);
                response.Message = "Update project access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            if (model.StartDate.HasValue && !(model.EndDate.HasValue) && selectedProject.EndDate.HasValue)
            {
                if (model.StartDate > selectedProject.EndDate)
                {
                    response.Message = "Project model data has problem";
                    response.AddError(localizer["Start Date should be before End Date."]);
                    return StatusCode(StatusCodes.Status500InternalServerError, response);
                }
            }
            if (model.EndDate.HasValue && !(model.StartDate.HasValue) && selectedProject.StartDate.HasValue)
            {
                if (model.EndDate < selectedProject.StartDate)
                {
                    response.Message = "Project model data has problem";
                    response.AddError(localizer["Start Date should be before End Date."]);
                    return StatusCode(StatusCodes.Status500InternalServerError, response);
                }
            }

            if (!isProjectPersonal && willBeProjectPersonal)
            {
                response.AddError(localizer["You can not change non-personal project to personal project."]);
                response.Message = "Non-personal project to project";
                return StatusCode(StatusCodes.Status406NotAcceptable, response);
            }

            List<int> projectDepartmentsIds = selectedProject.ProjectDepartments.Select(projectDepartment => projectDepartment.DepartmentId).ToList();

            List<int> shouldBeRemoved = projectDepartmentsIds.Except(model.DepartmentIds).ToList();
            List<int> shouldBeAdded = model.DepartmentIds.Except(projectDepartmentsIds).ToList();

            var modificationTime = DateTime.Now;

            selectedProject.Name = model.Name;
            selectedProject.Description = model.Description;
            selectedProject.EndDate = model.EndDate;
            selectedProject.StartDate = model.StartDate;
            selectedProject.Color = model.Color;
            selectedProject.ModifiedOn = modificationTime;
            selectedProject.ModifiedBy = userId;
            selectedProject.TeamId = model.TeamId;

            foreach (var rmProjectDepartment in shouldBeRemoved)
            {
                var selectedProjectDepartment = vwsDbContext.ProjectDepartments
                                                            .FirstOrDefault(projectDepartment => projectDepartment.DepartmentId == rmProjectDepartment &&
                                                                                                 projectDepartment.ProjectId == model.Id);

                vwsDbContext.DeleteProjectDepartment(selectedProjectDepartment);
            }

            foreach (var addProjectDepartment in shouldBeAdded)
                vwsDbContext.AddProjectDepartment(new ProjectDepartment() { ProjectId = model.Id, DepartmentId = addProjectDepartment });

            if (isProjectPersonal && !willBeProjectPersonal)
            {
                var projectMembers = vwsDbContext.ProjectMembers.Where(projectMember => projectMember.ProjectId == model.Id);
                foreach (var projectMember in projectMembers)
                    vwsDbContext.DeleteProjectMember(projectMember);
            }

            var newProjectHistory = new ProjectHistory()
            {
                ProjectId = model.Id,
                Event = "Project updated by {0}.",
                EventTime = modificationTime,
                CommaSepratedParameters = (await userManager.FindByIdAsync(userId.ToString())).UserName
            };

            vwsDbContext.AddProjectHistory(newProjectHistory);

            vwsDbContext.Save();

            var newProjectResponse = new ProjectResponseModel()
            {
                Id = selectedProject.Id,
                StatusId = selectedProject.StatusId,
                Name = selectedProject.Name,
                Description = selectedProject.Description,
                Color = selectedProject.Color,
                StartDate = selectedProject.StartDate,
                EndDate = selectedProject.EndDate,
                Guid = selectedProject.Guid,
                IsDelete = selectedProject.IsDeleted,
                TeamId = selectedProject.TeamId,
                ProjectImageId = selectedProject.ProjectImageId,
                DepartmentIds = model.DepartmentIds,
                NumberOfUpdates = vwsDbContext.ProjectHistories.Where(history => history.ProjectId == selectedProject.Id).Count(),
                Users = await GetProjectUsers(selectedProject.Id)
            };

            response.Value = newProjectResponse;
            response.Message = "Project updated successfully!";
            return Ok(response);
        }

        [HttpDelete]
        [Authorize]
        [Route("delete")]
        public async Task<IActionResult> DeleteProject(int id)
        {
            var response = new ResponseModel();

            Guid userId = LoggedInUserId.Value;

            var selectedProject = vwsDbContext.Projects.FirstOrDefault(project => project.Id == id);
            if (selectedProject == null || selectedProject.IsDeleted)
            {
                response.AddError(localizer["There is no project with given Id."]);
                response.Message = "Projet not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!HasAccessToProject(userId, id))
            {
                response.AddError(localizer["You are not a memeber of project."]);
                response.Message = "Project access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            if (selectedProject.TeamId == null && userId != selectedProject.CreateBy)
            {
                response.AddError(localizer["Deleting personal project just can be done by creator."]);
                response.Message = "Delete project access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            var modificationTime = DateTime.Now;

            selectedProject.IsDeleted = true;
            selectedProject.ModifiedBy = userId;
            selectedProject.ModifiedOn = modificationTime;

            var newProjectHistory = new ProjectHistory()
            {
                ProjectId = id,
                Event = "Project deleted by {0}.",
                EventTime = modificationTime,
                CommaSepratedParameters = (await userManager.FindByIdAsync(userId.ToString())).UserName
            };

            vwsDbContext.AddProjectHistory(newProjectHistory);

            vwsDbContext.Save();

            response.Message = "Project deleted successfully!";
            return Ok(response);
        }

        [HttpGet]
        [Authorize]
        [Route("get")]
        public async Task<ProjectResponseModel> Get(int Id)
        {
            Guid userId = LoggedInUserId.Value;

            var project = GetAllUserProjects(userId).FirstOrDefault(project => project.Id == Id);
            if (project == null)
                return new ProjectResponseModel();

            var response = new ProjectResponseModel()
            {
                Id = project.Id,
                Description = project.Description,
                Color = project.Color,
                EndDate = project.EndDate,
                Guid = project.Guid,
                IsDelete = project.IsDeleted,
                Name = project.Name,
                StartDate = project.StartDate,
                StatusId = project.StatusId,
                TeamId = project.TeamId,
                ProjectImageId = project.ProjectImageId,
                DepartmentIds = project.ProjectDepartments.Select(projectDepartment => projectDepartment.DepartmentId).ToList(),
                NumberOfUpdates = vwsDbContext.ProjectHistories.Where(history => history.ProjectId == project.Id).Count(),
                Users = await GetProjectUsers(project.Id)
            };

            return response;
        }

        [HttpGet]
        [Authorize]
        [Route("getActive")]
        public async Task<IEnumerable<ProjectResponseModel>> GetActiveProject()
        {
            var response = new List<ProjectResponseModel>();
            Guid userId = LoggedInUserId.Value;

            var userProjects = GetAllUserProjects(userId).Where(project => project.StatusId == (byte)SeedDataEnum.ProjectStatuses.Active);

            foreach (var project in userProjects)
            {
                response.Add(new ProjectResponseModel()
                {
                    Id = project.Id,
                    Description = project.Description,
                    Color = project.Color,
                    EndDate = project.EndDate,
                    Guid = project.Guid,
                    IsDelete = project.IsDeleted,
                    Name = project.Name,
                    StartDate = project.StartDate,
                    StatusId = project.StatusId,
                    TeamId = project.TeamId,
                    ProjectImageId = project.ProjectImageId,
                    DepartmentIds = project.ProjectDepartments.Select(projectDepartment => projectDepartment.DepartmentId).ToList(),
                    NumberOfUpdates = vwsDbContext.ProjectHistories.Where(history => history.ProjectId == project.Id).Count(),
                    Users = await GetProjectUsers(project.Id)
                });
            }

            return response;
        }

        [HttpGet]
        [Authorize]
        [Route("getHold")]
        public async Task<IEnumerable<ProjectResponseModel>> GetHoldProject()
        {
            var response = new List<ProjectResponseModel>();
            Guid userId = LoggedInUserId.Value;

            var userProjects = GetAllUserProjects(userId).Where(project => project.StatusId == (byte)SeedDataEnum.ProjectStatuses.Hold);

            foreach (var project in userProjects)
            {
                response.Add(new ProjectResponseModel()
                {
                    Id = project.Id,
                    Description = project.Description,
                    Color = project.Color,
                    EndDate = project.EndDate,
                    Guid = project.Guid,
                    IsDelete = project.IsDeleted,
                    Name = project.Name,
                    StartDate = project.StartDate,
                    StatusId = project.StatusId,
                    TeamId = project.TeamId,
                    ProjectImageId = project.ProjectImageId,
                    DepartmentIds = project.ProjectDepartments.Select(projectDepartment => projectDepartment.DepartmentId).ToList(),
                    NumberOfUpdates = vwsDbContext.ProjectHistories.Where(history => history.ProjectId == project.Id).Count(),
                    Users = await GetProjectUsers(project.Id)
                });
            }

            return response;
        }

        [HttpGet]
        [Authorize]
        [Route("getDoneOrArchived")]
        public async Task<IEnumerable<ProjectResponseModel>> GetDoneOrArchivedProject()
        {
            var response = new List<ProjectResponseModel>();
            Guid userId = LoggedInUserId.Value;

            var userProjects = GetAllUserProjects(userId).Where(project => project.StatusId == (byte)SeedDataEnum.ProjectStatuses.DoneOrArchived);

            foreach (var project in userProjects)
            {
                response.Add(new ProjectResponseModel()
                {
                    Id = project.Id,
                    Description = project.Description,
                    Color = project.Color,
                    EndDate = project.EndDate,
                    Guid = project.Guid,
                    IsDelete = project.IsDeleted,
                    Name = project.Name,
                    StartDate = project.StartDate,
                    StatusId = project.StatusId,
                    TeamId = project.TeamId,
                    ProjectImageId = project.ProjectImageId,
                    DepartmentIds = project.ProjectDepartments.Select(projectDepartment => projectDepartment.DepartmentId).ToList(),
                    Users = await GetProjectUsers(project.Id)
                });
            }

            return response;
        }

        [HttpGet]
        [Authorize]
        [Route("getUsersCanBeAddedToProject")]
        public async Task<IActionResult> GetUsersCanBeAddedToProject(int Id)
        {
            var response = new ResponseModel<List<UserModel>>();
            var users = new List<UserModel>();

            var userId = LoggedInUserId.Value;

            var selectedProject = vwsDbContext.Projects.Include(project => project.ProjectDepartments)
                                                       .FirstOrDefault(project => project.Id == Id);

            if (selectedProject == null || selectedProject.IsDeleted)
            {
                response.AddError(localizer["There is no project with given Id."]);
                response.Message = "Projet not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (selectedProject.TeamId != null)
            {
                response.AddError(localizer["Adding user is just for personal projects."]);
                response.Message = "Non-Personal project";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!HasAccessToProject(userId, Id))
            {
                response.AddError(localizer["You are not a memeber of project."]);
                response.Message = "Project access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            var availableUsers = GetAvailableUsersToAddProject(selectedProject, userId);

            foreach (var availableUserId in availableUsers)
            {
                var user = await userManager.FindByIdAsync(availableUserId.ToString());
                var userProfile = await vwsDbContext.GetUserProfileAsync(availableUserId);
                users.Add(new UserModel()
                {
                    UserId = availableUserId,
                    ProfileImageId = userProfile.ProfileImageId,
                    UserName = user.UserName
                });
            }

            response.Value = users;
            response.Message = "Users returned successfully!";
            return Ok(response);
        }

        [HttpGet]
        [Authorize]
        [Route("getProjectMembers")]
        public async Task<IActionResult> GetProjectMembers(int projectId)
        {
            var response = new ResponseModel<List<UserModel>>();
            var members = new List<UserModel>();

            var userId = LoggedInUserId.Value;

            var userProjects = GetAllUserProjects(LoggedInUserId.Value);

            if (!userProjects.Any(project => project.Id == projectId))
            {
                response.AddError(localizer["There is no project with given Id."]);
                response.Message = "Project not found";
                return BadRequest(response);
            }

            List<UserProfile> users = new List<UserProfile>();

            var selectedProject = vwsDbContext.Projects.Include(project => project.ProjectDepartments).FirstOrDefault(project => project.Id == projectId);
            if (selectedProject.TeamId == null)
            {
                users = vwsDbContext.ProjectMembers.Include(projectMember => projectMember.UserProfile)
                                                   .Where(projectMember => projectMember.ProjectId == selectedProject.Id && !projectMember.IsDeleted)
                                                   .Select(projectMember => projectMember.UserProfile).ToList();
            }
            else if (selectedProject.ProjectDepartments.Count == 0)
            {
                users = vwsDbContext.TeamMembers.Include(teamMember => teamMember.UserProfile)
                                                .Where(teamMember => teamMember.TeamId == selectedProject.TeamId && !teamMember.IsDeleted)
                                                .Select(teamMember => teamMember.UserProfile).ToList();
            }
            else
            {
                foreach (var projectDepartment in selectedProject.ProjectDepartments)
                {
                    var selectedDepartment = vwsDbContext.Departments.FirstOrDefault(department => department.Id == projectDepartment.DepartmentId);
                    users.AddRange(vwsDbContext.DepartmentMembers.Include(departmentMember => departmentMember.UserProfile)
                                                                 .Where(departmentMember => departmentMember.DepartmentId == selectedDepartment.Id && !departmentMember.IsDeleted)
                                                                 .Select(departmentMember => departmentMember.UserProfile));
                }
            }

            foreach (var user in users)
            {
                members.Add(new UserModel()
                {
                    UserId = user.UserId,
                    UserName = (await userManager.FindByIdAsync(user.UserId.ToString())).UserName,
                    ProfileImageId = user.ProfileImageId
                });
            }

            response.Message = "Members returned successfully!";
            response.Value = members;

            return Ok(response);
        }

        [HttpPost]
        [Authorize]
        [Route("addUserToProject")]
        public async Task<IActionResult> AddUserToProject([FromBody] AddUserToProjectModel model)
        {
            var response = new ResponseModel();

            var userId = LoggedInUserId.Value;

            var selectedProject = vwsDbContext.Projects.Include(project => project.ProjectDepartments)
                                                       .FirstOrDefault(project => project.Id == model.ProjectId);

            if (selectedProject == null || selectedProject.IsDeleted)
            {
                response.AddError(localizer["There is no project with given Id."]);
                response.Message = "Projet not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (selectedProject.TeamId != null)
            {
                response.AddError(localizer["Adding user is just for personal projects."]);
                response.Message = "Non-Personal project";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!HasAccessToProject(userId, model.ProjectId))
            {
                response.AddError(localizer["You are not a memeber of project."]);
                response.Message = "Project access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            var selectedProjectMember = vwsDbContext.ProjectMembers.FirstOrDefault(projectMember => projectMember.UserProfileId == model.UserId &&
                                                                                         projectMember.ProjectId == model.ProjectId &&
                                                                                         !projectMember.IsDeleted);

            if (selectedProjectMember != null && selectedProjectMember.IsPermittedByCreator == true)
            {
                response.AddError(localizer["User you want to add, is already a member of selected project."]);
                response.Message = "Already member of project";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }
            else if (selectedProjectMember != null && selectedProjectMember.IsPermittedByCreator == null)
            {
                if (selectedProject.CreateBy != userId)
                {
                    response.AddError(localizer["User you want to add, is already a member of selected project."]);
                    response.Message = "Already member of project";
                    return StatusCode(StatusCodes.Status400BadRequest, response);
                }

                selectedProjectMember.IsPermittedByCreator = true;
                selectedProjectMember.PermittedOn = DateTime.Now;

                vwsDbContext.Save();

                response.Message = "User added successfully!";
                return Ok(response);
            }

            var availableUsers = GetAvailableUsersToAddProject(selectedProject, userId);
            if (!availableUsers.Contains(model.UserId))
            {
                response.AddError(localizer["This user can not be added to project."]);
                response.Message = "Can not be added project";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var newPorjectMember = new ProjectMember()
            {
                CreatedOn = DateTime.Now,
                IsDeleted = false,
                ProjectId = model.ProjectId,
                UserProfileId = model.UserId,
                IsPermittedByCreator = (userId == selectedProject.CreateBy) ? (bool?)true : null
            };

            await vwsDbContext.AddProjectMemberAsync(newPorjectMember);

            var newProjectHistory = new ProjectHistory()
            {
                ProjectId = model.ProjectId,
                Event = "{0} added {1} to the project.",
                EventTime = newPorjectMember.CreatedOn,
                CommaSepratedParameters = (await userManager.FindByIdAsync(userId.ToString())).UserName + "," + (await userManager.FindByIdAsync(model.UserId.ToString())).UserName
            };

            vwsDbContext.AddProjectHistory(newProjectHistory);

            vwsDbContext.Save();

            response.Message = "User added successfully!";
            return Ok(response);
        }

        [HttpPost]
        [Authorize]
        [Route("uploadProjectImage")]
        public async Task<IActionResult> UploadProjectImage(IFormFile image, int projectId)
        {
            var response = new ResponseModel();

            string[] types = { "png", "jpg", "jpeg" };

            var files = Request.Form.Files.ToList();

            Guid userId = LoggedInUserId.Value;

            if (files.Count > 1)
            {
                response.AddError(localizer["There is more than one file."]);
                response.Message = "Too many files passed";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }
            if (files.Count == 0 && image == null)
            {
                response.AddError(localizer["You did not upload an image."]);
                response.Message = "There is no image";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }
            var uploadedImage = files.Count == 0 ? image : files[0];

            var selectedProject = vwsDbContext.Projects.Include(project => project.ProjectImage).FirstOrDefault(project => project.Id == projectId);
            if (selectedProject == null || selectedProject.IsDeleted)
            {
                response.AddError(localizer["There is no project with given Id."]);
                response.Message = "Project not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!HasAccessToProject(userId, projectId))
            {
                response.Message = "Project access denied";
                response.AddError(localizer["You are not a memeber of project."]);
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            bool isProjectPersonal = selectedProject.TeamId == null ? true : false;

            if (isProjectPersonal && userId != selectedProject.CreateBy)
            {
                response.AddError(localizer["Updating personal project just can be done by creator."]);
                response.Message = "Update project access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            ResponseModel<File> fileResponse;

            if (selectedProject.ProjectImage != null)
            {
                fileResponse = await fileManager.WriteFile(uploadedImage, userId, "profileImages", (int)selectedProject.ProjectImageId, types.ToList());
                if (fileResponse.HasError)
                {
                    foreach (var error in fileResponse.Errors)
                        response.AddError(localizer[error]);
                    response.Message = "Error in writing file";
                    return StatusCode(StatusCodes.Status500InternalServerError, response);
                }
                selectedProject.ProjectImage.RecentFileId = fileResponse.Value.Id;
            }
            else
            {
                var time = DateTime.Now;
                var newFileContainer = new FileContainer
                {
                    ModifiedOn = time,
                    CreatedOn = time,
                    CreatedBy = userId,
                    ModifiedBy = userId,
                    Guid = Guid.NewGuid()
                };
                await vwsDbContext.AddFileContainerAsync(newFileContainer);
                vwsDbContext.Save();
                fileResponse = await fileManager.WriteFile(uploadedImage, userId, "profileImages", newFileContainer.Id, types.ToList());
                if (fileResponse.HasError)
                {
                    foreach (var error in fileResponse.Errors)
                        response.AddError(localizer[error]);
                    response.Message = "Error in writing file";
                    vwsDbContext.DeleteFileContainer(newFileContainer);
                    vwsDbContext.Save();
                    return StatusCode(StatusCodes.Status500InternalServerError, response);
                }
                newFileContainer.RecentFileId = fileResponse.Value.Id;
                selectedProject.ProjectImageId = newFileContainer.Id;
            }
            selectedProject.ModifiedBy = LoggedInUserId.Value;
            selectedProject.ModifiedOn = DateTime.Now;

            var newProjectHistory = new ProjectHistory()
            {
                ProjectId = projectId,
                Event = "Added new project image for project by {0}.",
                EventTime = selectedProject.ModifiedOn,
                CommaSepratedParameters = (await userManager.FindByIdAsync(userId.ToString())).UserName
            };

            vwsDbContext.AddProjectHistory(newProjectHistory);

            vwsDbContext.Save();
            response.Message = "Project image added successfully!";
            return Ok(response);
        }

        [HttpPut]
        [Authorize]
        [Route("changeStatus")]
        public async Task<IActionResult> ChangeProjectStatus(int id, byte statusId)
        {
            var response = new ResponseModel();

            var selectedProject = vwsDbContext.Projects.FirstOrDefault(project => project.Id == id);
            if (selectedProject == null || selectedProject.IsDeleted)
            {
                response.Message = "Project not found";
                response.AddError(localizer["There is no project with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (statusId < 1 || statusId > 3)
            {
                response.Message = "Invalid tatus id";
                response.AddError(localizer["Status id is not valid."]);
            }

            var userId = LoggedInUserId.Value;

            if (!HasAccessToProject(userId, id))
            {
                response.Message = "Project access denied";
                response.AddError(localizer["You are not a memeber of project."]);
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            bool isProjectPersonal = selectedProject.TeamId == null ? true : false;

            if (isProjectPersonal && userId != selectedProject.CreateBy)
            {
                response.AddError(localizer["Updating personal project just can be done by creator."]);
                response.Message = "Update project access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            var lastStatus = selectedProject.StatusId;

            selectedProject.StatusId = statusId;
            selectedProject.ModifiedOn = DateTime.Now;
            selectedProject.ModifiedBy = userId;

            var newProjectHistory = new ProjectHistory()
            {
                ProjectId = id,
                Event = "Project status changed from {0} to {1} by {2}.",
                EventTime = selectedProject.ModifiedOn,
                CommaSepratedParameters = (SeedDataEnum.ProjectStatuses)lastStatus +
                                          "," + (SeedDataEnum.ProjectStatuses)selectedProject.StatusId +
                                          "," + (await userManager.FindByIdAsync(userId.ToString())).UserName
            };

            vwsDbContext.AddProjectHistory(newProjectHistory);

            vwsDbContext.Save();

            response.Message = "Project status updated successfully!";
            return Ok(response);
        }

        [HttpGet]
        [Authorize]
        [Route("getProjectUserRequests")]
        public async Task<IActionResult> GetProjectUserRequests(int id)
        {
            var response = new ResponseModel<List<Object>>();
            var users = new List<Object>();

            var userId = LoggedInUserId.Value;

            var selectedProject = vwsDbContext.Projects.FirstOrDefault(project => project.Id == id);

            if (selectedProject == null || selectedProject.IsDeleted)
            {
                response.Message = "Project not found";
                response.AddError(localizer["There is no project with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!HasAccessToProject(userId, id))
            {
                response.Message = "Project access denied";
                response.AddError(localizer["You are not a memeber of project."]);
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            if (selectedProject.TeamId != null)
            {
                response.Message = "Non-personal project";
                response.AddError(localizer["Project is not personal."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (userId != selectedProject.CreateBy)
            {
                response.Message = "Not creator";
                response.AddError(localizer["You are not project creator."]);
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            var requestedUsers = vwsDbContext.ProjectMembers.Include(projectMember => projectMember.UserProfile)
                                                            .Where(projectMember => projectMember.ProjectId == id &&
                                                                                    !projectMember.IsDeleted &&
                                                                                    projectMember.IsPermittedByCreator == null);

            foreach (var user in requestedUsers)
            {
                users.Add(new
                {
                    Id = user.Id,
                    UserInfo = new UserModel()
                    {
                        ProfileImageId = user.UserProfile.ProfileImageId,
                        UserId = user.UserProfileId,
                        UserName = (await userManager.FindByIdAsync(user.UserProfileId.ToString())).UserName
                    }
                });
            }

            response.Value = users;
            response.Message = "Users returned successfully!";

            return Ok(response);
        }

        [HttpPut]
        [Authorize]
        [Route("changeProjectMemberPermisssion")]
        public async Task<IActionResult> ChangeProjectMemberPermisssion(int projectMemberId, bool hasAccess)
        {
            var response = new ResponseModel();

            var userId = LoggedInUserId.Value;

            var selectedProjecMember = vwsDbContext.ProjectMembers.Include(projectMember => projectMember.Project).FirstOrDefault(projectMember => projectMember.Id == projectMemberId);

            if (selectedProjecMember == null || selectedProjecMember.IsDeleted)
            {
                response.AddError(localizer["There is not project member with such id."]);
                response.Message = "Project member not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (selectedProjecMember.Project.CreateBy != userId)
            {
                response.Message = "Not creator";
                response.AddError(localizer["You are not project creator."]);
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            if (selectedProjecMember.Project.CreateBy == userId)
            {
                response.Message = "Creator can not change his permission.";
                response.AddError(localizer["You are the project creator and can not change your access permission."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            selectedProjecMember.IsPermittedByCreator = hasAccess;
            vwsDbContext.Save();

            return Ok(response);
        }

        [HttpGet]
        [Authorize]
        [Route("getProjectHistory")]
        public IActionResult GetProjectHistory(int id)
        {
            var userId = LoggedInUserId.Value;
            var response = new ResponseModel<List<ProjectHistoryModel>>();

            var selectedProject = vwsDbContext.Projects.FirstOrDefault(project => project.Id == id);

            if (selectedProject == null || selectedProject.IsDeleted)
            {
                response.Message = "Project not found";
                response.AddError(localizer["There is no project with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!HasAccessToProject(userId, id))
            {
                response.Message = "Project access denied";
                response.AddError(localizer["You are not a memeber of project."]);
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            if (selectedProject.TeamId == null && selectedProject.CreateBy != userId)
            {
                response.Message = "Not creator";
                response.AddError(localizer["You are not project creator."]);
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            var events = new List<ProjectHistoryModel>();
            var projectEvents = vwsDbContext.ProjectHistories.Where(projectHistory => projectHistory.ProjectId == id);
            foreach (var projectEvent in projectEvents)
            {
                var parameters = projectEvent.CommaSepratedParameters.Split(',');
                events.Add(new ProjectHistoryModel()
                {
                    Message = String.Format(localizer[projectEvent.Event], parameters),
                    Time = projectEvent.EventTime
                });
            }

            response.Message = "History returned successfully!";
            response.Value = events;
            return Ok(response);
        }
    }
}
