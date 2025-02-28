﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using vws.web.Domain;
using vws.web.Domain._base;
using vws.web.Domain._team;
using vws.web.Models._team;
using vws.web.Models;
using vws.web.Repositories;
using vws.web.Domain._file;
using Microsoft.EntityFrameworkCore;
using vws.web.Enums;
using vws.web.Models._department;
using vws.web.Services._department;
using vws.web.Services._team;
using static vws.web.EmailTemplates.EmailTemplateTypes;
using Microsoft.Extensions.Configuration;
using System.Net;
using Newtonsoft.Json;
using vws.web.Services;
using vws.web.Models._project;
using vws.web.Services._project;
using vws.web.Models._task;
using vws.web.Services._task;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using Microsoft.Extensions.Logging;

namespace vws.web.Controllers._team
{
    [Route("{culture:culture}/[controller]")]
    [ApiController]
    public class TeamController : BaseController
    {
        #region Feilds
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IDepartmentManagerService _departmentManager;
        private readonly IStringLocalizer<TeamController> _localizer;
        private readonly IVWS_DbContext _vwsDbContext;
        private readonly IFileManager _fileManager;
        private readonly ITeamManagerService _teamManager;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;
        private readonly IPermissionService _permissionService;
        private readonly IProjectManagerService _projectManager;
        private readonly ITaskManagerService _taskManager;
        private readonly INotificationService _notificationService;
        private readonly EmailAddressAttribute _emailChecker;
        private readonly IImageService _imageService;
        private readonly ILogger<TeamController> _logger;
        private readonly IUserService _userService;
        #endregion

        #region Ctor
        public TeamController(UserManager<ApplicationUser> userManager, IStringLocalizer<TeamController> localizer,
            IVWS_DbContext vwsDbContext, IFileManager fileManager, IDepartmentManagerService departmentManager,
            ITeamManagerService teamManager, IEmailSender emailSender, IConfiguration configuration, IPermissionService permissionService,
            IProjectManagerService projectManager, ITaskManagerService taskManagerService,
            INotificationService notificationService, IImageService imageService, ILogger<TeamController> logger,
            IUserService userService)
        {
            _userManager = userManager;
            _localizer = localizer;
            _vwsDbContext = vwsDbContext;
            _fileManager = fileManager;
            _departmentManager = departmentManager;
            _teamManager = teamManager;
            _emailSender = emailSender;
            _configuration = configuration;
            _permissionService = permissionService;
            _projectManager = projectManager;
            _taskManager = taskManagerService;
            _notificationService = notificationService;
            _imageService = imageService;
            _emailChecker = new EmailAddressAttribute();
            _logger = logger;
            _userService = userService;
        }
        #endregion

        #region PrivateMethods
        private void CreateTeamTaskStatuses(int teamId)
        {
            _vwsDbContext.AddTaskStatus(new Domain._task.TaskStatus() { EvenOrder = 2, ProjectId = null, UserProfileId = null, TeamId = teamId, Title = "To Do" });
            _vwsDbContext.AddTaskStatus(new Domain._task.TaskStatus() { EvenOrder = 4, ProjectId = null, UserProfileId = null, TeamId = teamId, Title = "Doing" });
            _vwsDbContext.AddTaskStatus(new Domain._task.TaskStatus() { EvenOrder = 6, ProjectId = null, UserProfileId = null, TeamId = teamId, Title = "Done" });

            _vwsDbContext.Save();
        }

        private async Task<List<DepartmentResponseModel>> GetDepartments(int teamId)
        {
            var result = new List<DepartmentResponseModel>();

            var departments = _vwsDbContext.Departments.Where(department => department.TeamId == teamId && !department.IsDeleted);

            foreach (var department in departments)
            {
                result.Add(new DepartmentResponseModel()
                {
                    Id = department.Id,
                    Color = department.Color,
                    CreatedBy = (await _vwsDbContext.GetUserProfileAsync(department.CreatedBy)).NickName,
                    DepartmentImageGuid = department.DepartmentImageGuid,
                    Description = department.Description,
                    CreatedOn = department.CreatedOn,
                    Guid = department.Guid,
                    ModifiedBy = (await _vwsDbContext.GetUserProfileAsync(department.ModifiedBy)).NickName,
                    ModifiedOn = department.ModifiedOn,
                    Name = department.Name,
                    TeamId = department.TeamId,
                    Users = await _departmentManager.GetDepartmentMembers(department.Id)
                });
            }

            return result;
        }

        private async Task CreateTeamDepartments(List<DepartmentBaseModel> models, int teamId)
        {
            foreach (var model in models)
            {
                DepartmentModel departmentModel = new DepartmentModel()
                {
                    Color = model.Color,
                    Description = model.Description,
                    Name = model.Name,
                    TeamId = teamId,
                    Users = model.Users
                };
                departmentModel.TeamId = teamId;
                await _departmentManager.CreateDepartment(departmentModel, LoggedInUserId.Value);
            }
        }

        private async Task SendJoinTeamInvitaionLinks(List<string> emails, int teamId)
        {
            string emailErrorMessage;
            Guid linkGuid = Guid.NewGuid();
            var newInviteLink = new TeamInviteLink()
            {
                TeamId = teamId,
                CreatedBy = LoggedInUserId.Value,
                ModifiedBy = LoggedInUserId.Value,
                CreatedOn = DateTime.UtcNow,
                ModifiedOn = DateTime.UtcNow,
                LinkGuid = linkGuid,
                IsRevoked = false
            };

            await _vwsDbContext.AddTeamInviteLinkAsync(newInviteLink);
            _vwsDbContext.Save();
            SendEmailModel emailModelTemplate = new SendEmailModel
            {
                FromEmail = _configuration["EmailSender:RegistrationEmail:EmailAddress"],
                ToEmail = "",
                Subject = "Join Team",
                Body = $"{_configuration["Angular:Url"]}/inviteTeam?invitationCode=" + linkGuid.ToString(),
                Credential = new NetworkCredential
                {
                    UserName = _configuration["EmailSender:RegistrationEmail:UserName"],
                    Password = _configuration["EmailSender:RegistrationEmail:Password"]
                },
                IsBodyHtml = false
            };
            Task.Run(async () =>
            {
                foreach (var email in emails)
                {
                    emailModelTemplate.ToEmail = email;
                    await _emailSender.SendEmailAsync(emailModelTemplate, out emailErrorMessage);
                }
            });
        }

        private async Task SendCreateTeamEmail(int teamId)
        {
            var selectedTeam = _vwsDbContext.Teams.FirstOrDefault(team => team.Id == teamId);
            var users = (await _teamManager.GetTeamMembers(teamId)).Select(user => user.UserId).ToList();
            users = users.Distinct().ToList();
            users.Remove(LoggedInUserId.Value);
            string emailMessage = "<b>«{0}»</b> created new team with name <b>«{1}»</b>.";
            string departmentEmailMessage = "<b>«{0}»</b> created new department with name <b>«{1}»</b>.";
            string[] arguments = { LoggedInNickName, selectedTeam.Name };
            await _notificationService.SendMultipleEmails((int)EmailTemplateEnum.NotificationEmail, users, emailMessage, "Team Creation", arguments);

            var departments = _vwsDbContext.Departments.Include(department => department.DepartmentMembers)
                                                       .Where(department => department.TeamId == teamId && !department.IsDeleted);
            foreach (var department in departments)
            {
                var departmentUsers = department.DepartmentMembers.Select(department => department.UserProfileId).ToList();
                departmentUsers = departmentUsers.Distinct().ToList();
                users.Remove(LoggedInUserId.Value);
                string[] args = { LoggedInNickName, department.Name };
                await _notificationService.SendMultipleEmails((int)EmailTemplateEnum.NotificationEmail, departmentUsers, departmentEmailMessage, "Department Creation", args);
            }
        }

        private void DeleteTeamTasks(int teamId, DateTime deleteTime)
        {
            var teamTasks = _vwsDbContext.GeneralTasks.Where(task => task.TeamId == teamId && !task.IsDeleted);

            foreach (var teamTask in teamTasks)
            {
                teamTask.IsDeleted = true;
                teamTask.ModifiedBy = LoggedInUserId.Value;
                teamTask.ModifiedOn = deleteTime;
            }
            _vwsDbContext.Save();
        }

        private void DeleteTeamProjects(int teamId, DateTime deleteTime)
        {
            var teamProjects = _vwsDbContext.Projects.Where(project => project.TeamId == teamId && !project.IsDeleted);

            foreach (var teamProject in teamProjects)
            {
                teamProject.IsDeleted = true;
                teamProject.ModifiedBy = LoggedInUserId.Value;
                teamProject.ModifiedOn = deleteTime;
            }
            _vwsDbContext.Save();
        }
        private void DeleteTeamDepartments(int teamId, DateTime deleteTime)
        {
            var teamDepartments = _vwsDbContext.Departments.Where(department => department.TeamId == teamId && !department.IsDeleted);

            foreach (var teamDepartment in teamDepartments)
            {
                teamDepartment.IsDeleted = true;
                teamDepartment.ModifiedBy = LoggedInUserId.Value;
                teamDepartment.ModifiedOn = deleteTime;
            }
            _vwsDbContext.Save();
        }

        private void DeleteTeamEvents(int teamId, DateTime deleteTime)
        {
            var teamEvents = _vwsDbContext.Events.Where(_event => _event.TeamId == teamId && !_event.IsDeleted);

            foreach (var teamEvent in teamEvents)
            {
                teamEvent.IsDeleted = true;
                teamEvent.ModifiedBy = LoggedInUserId.Value;
                teamEvent.ModifiedOn = deleteTime;
            }
            _vwsDbContext.Save();
        }

        #endregion

        #region TeamAPIS
        [HttpPost]
        [Authorize]
        [Route("create")]
        public async Task<IActionResult> CreateTeam([FromBody] TeamModel model)
        {
            var response = new ResponseModel<TeamResponseModel>();
            Guid userId = LoggedInUserId.Value;

            var allDepartmentUsers = new List<Guid>();
            foreach (var department in model.Departments)
            {
                department.Users = department.Users.Distinct().ToList();
                allDepartmentUsers.AddRange(department.Users);
            }
            allDepartmentUsers = allDepartmentUsers.Distinct().ToList();
            allDepartmentUsers.Remove(userId);

            model.Users = model.Users.Union(allDepartmentUsers).ToList();
            model.Users = model.Users.Distinct().ToList();
            model.Users.Remove(userId);

            #region CheckModel
            if (!String.IsNullOrEmpty(model.Description) && model.Description.Length > 2000)
            {
                response.Message = "Team model data has problem.";
                response.AddError(_localizer["Length of description is more than 2000 characters."]);
            }
            if (model.Name.Length > 500)
            {
                response.Message = "Team model data has problem.";
                response.AddError(_localizer["Length of title is more than 500 characters."]);
            }
            if (!String.IsNullOrEmpty(model.Color) && model.Color.Length > 6)
            {
                response.Message = "Team model data has problem.";
                response.AddError(_localizer["Length of color is more than 6 characters."]);
            }
            foreach (var email in model.EmailsForInvite)
            {
                if (!_emailChecker.IsValid(email))
                {
                    response.AddError(_localizer["Invalid emails."]);
                    response.Message = "Invalid emails";
                    break;
                }
            }
            var teammates = _teamManager.GetUserTeammates(LoggedInUserId.Value);
            if (teammates.Intersect(model.Users).Count() != model.Users.Count)
            {
                response.Message = "Team model data has problem.";
                response.AddError(_localizer["Invalid team users."]);
            }
            var hasTeamWithSameName = _vwsDbContext.TeamMembers.Any(teamMember => teamMember.UserProfileId == userId &&
                                                                    teamMember.Team.Name == model.Name &&
                                                                    teamMember.Team.IsDeleted == false &&
                                                                    teamMember.IsDeleted == false);
            if (hasTeamWithSameName)
            {
                response.Message = "Team model data has problem.";
                response.AddError(_localizer["You are a member of a team with that name."]);
            }

            if (response.HasError)
                return StatusCode(StatusCodes.Status400BadRequest, response);
            #endregion

            List<string> userEmails = new List<string>();
            userEmails.Add((await _userManager.FindByIdAsync(LoggedInUserId.Value.ToString())).Email);
            foreach (var modelUser in model.Users)
                userEmails.Add((await _userManager.FindByIdAsync(modelUser.ToString())).Email);
            model.EmailsForInvite = model.EmailsForInvite.Except(userEmails).ToList();

            var newTeam = await _teamManager.CreateTeam(model, userId);

            await CreateTeamDepartments(model.Departments, newTeam.Id);

            CreateTeamTaskStatuses(newTeam.Id);

            await SendJoinTeamInvitaionLinks(model.EmailsForInvite, newTeam.Id);

            #region History
            var newHistory = new TeamHistory()
            {
                TeamId = newTeam.Id,
                EventTime = newTeam.CreatedOn,
                EventBody = "Team {0} created by {1}."
            };
            _vwsDbContext.AddTeamHistory(newHistory);
            _vwsDbContext.Save();

            var user = await _vwsDbContext.GetUserProfileAsync(userId);
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.Text,
                Body = newTeam.Name,
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.User,
                Body = JsonConvert.SerializeObject(new UserModel()
                {
                    NickName = user.NickName,
                    ProfileImageGuid = user.ProfileImageGuid,
                    UserId = user.UserId
                }),
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            #endregion

            await SendCreateTeamEmail(newTeam.Id);

            var users = (await _teamManager.GetTeamMembers(newTeam.Id)).Select(user => user.UserId).ToList();
            users = users.Distinct().ToList();
            users.Remove(LoggedInUserId.Value);
            _notificationService.SendMultipleNotification(users, (byte)SeedDataEnum.NotificationTypes.Team, newHistory.Id);

            var newTeamResponse = new TeamResponseModel()
            {
                Id = newTeam.Id,
                TeamTypeId = newTeam.TeamTypeId,
                Name = newTeam.Name,
                Description = newTeam.Description,
                Color = newTeam.Color,
                CreatedBy = _userService.GetUser(newTeam.CreatedBy),
                ModifiedBy = _userService.GetUser(newTeam.ModifiedBy),
                CreatedOn = newTeam.CreatedOn,
                ModifiedOn = newTeam.ModifiedOn,
                Guid = newTeam.Guid,
                TeamImageGuid = newTeam.TeamImageGuid,
                NumberOfDepartments = _vwsDbContext.Departments.Where(department => department.TeamId == newTeam.Id && !department.IsDeleted).Count(),
                NumberOfMembers = _vwsDbContext.TeamMembers.Where(teamMember => teamMember.TeamId == newTeam.Id && !teamMember.IsDeleted).Count(),
                NumberOfTasks = _vwsDbContext.GeneralTasks.Where(task => task.TeamId == newTeam.Id && !task.IsDeleted).Count(),
                NumberOfProjects = _vwsDbContext.Projects.Where(project => project.TeamId == newTeam.Id && !project.IsDeleted).Count(),
                Users = await _teamManager.GetTeamMembers(newTeam.Id),
                Departments = await GetDepartments(newTeam.Id)
            };

            response.Value = newTeamResponse;
            response.Message = "Team created successfully!";
            return Ok(response);
        }

        [HttpPut]
        [Authorize]
        [Route("updateTeamName")]
        public async Task<IActionResult> UpdateTeamName(int id, [FromBody] StringModel model)
        {
            string newName = model.Value;
            var response = new ResponseModel();
            Guid userId = LoggedInUserId.Value;

            if (String.IsNullOrEmpty(newName) || newName.Length > 500)
            {
                response.Message = "Team model data has problem.";
                response.AddError(_localizer["Team name can not be empty and should have less than 500 characters."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var selectedTeam = await _vwsDbContext.GetTeamAsync(id);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.Message = "Team not found";
                response.AddError(_localizer["There is no team with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }
            if (!_permissionService.HasAccessToTeam(userId, id))
            {
                response.Message = "Access team is forbidden";
                response.AddError(_localizer["You are not a member of team."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (selectedTeam.Name == newName)
            {
                response.Message = "Duplicate data";
                return Ok(response);
            }

            var hasTeamWithSameName = _vwsDbContext.TeamMembers.Any(teamMember => teamMember.UserProfileId == userId &&
                                                                    teamMember.Team.Name == newName &&
                                                                    teamMember.Team.IsDeleted == false &&
                                                                    teamMember.IsDeleted == false);
            if (newName != selectedTeam.Name && hasTeamWithSameName)
            {
                response.Message = "Team model data has problem.";
                response.AddError(_localizer["You are a member of a team with that name."]);
            }


            var lastName = selectedTeam.Name;

            selectedTeam.ModifiedBy = userId;
            selectedTeam.ModifiedOn = DateTime.UtcNow;
            selectedTeam.Name = newName;
            _vwsDbContext.Save();

            #region History
            var newHistory = new TeamHistory()
            {
                TeamId = selectedTeam.Id,
                EventTime = selectedTeam.ModifiedOn,
                EventBody = "Team name updated from {0} to {1} by {2}."
            };
            _vwsDbContext.AddTeamHistory(newHistory);
            _vwsDbContext.Save();

            var user = await _vwsDbContext.GetUserProfileAsync(userId);
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.Text,
                Body = lastName,
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.Text,
                Body = selectedTeam.Name,
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.User,
                Body = JsonConvert.SerializeObject(new UserModel()
                {
                    NickName = user.NickName,
                    ProfileImageGuid = user.ProfileImageGuid,
                    UserId = user.UserId
                }),
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            #endregion

            var users = (await _teamManager.GetTeamMembers(id)).Select(user => user.UserId).ToList();
            users = users.Distinct().ToList();
            users.Remove(LoggedInUserId.Value);
            string emailMessage = "<b>«{0}»</b> updated team name from <b>«{1}»</b> to <b>«{2}»</b>.";
            string[] arguments = { LoggedInNickName, lastName, selectedTeam.Name };
            await _notificationService.SendMultipleEmails((int)EmailTemplateEnum.NotificationEmail, users, emailMessage, "Team Update", arguments);

            _notificationService.SendMultipleNotification(users, (byte)SeedDataEnum.NotificationTypes.Team, newHistory.Id);

            response.Message = "Team name updated successfully!";
            return Ok(response);
        }

        [HttpPut]
        [Authorize]
        [Route("updateTeamDescription")]
        public async Task<IActionResult> UpdateTeamDescription(int id, [FromBody] StringModel model)
        {
            string newDescription = model.Value;
            var response = new ResponseModel();
            Guid userId = LoggedInUserId.Value;

            if (!String.IsNullOrEmpty(newDescription) && newDescription.Length > 2000)
            {
                response.Message = "Team model data has problem.";
                response.AddError(_localizer["Length of description is more than 2000 characters."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var selectedTeam = await _vwsDbContext.GetTeamAsync(id);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.Message = "Team not found";
                response.AddError(_localizer["There is no team with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }
            if (!_permissionService.HasAccessToTeam(userId, id))
            {
                response.Message = "Access team is forbidden";
                response.AddError(_localizer["You are not a member of team."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (selectedTeam.Description == newDescription)
            {
                response.Message = "Duplicate data";
                return Ok(response);
            }

            var lastDescription = selectedTeam.Description;

            selectedTeam.ModifiedBy = userId;
            selectedTeam.ModifiedOn = DateTime.UtcNow;
            selectedTeam.Description = newDescription;
            _vwsDbContext.Save();

            #region History
            var newHistory = new TeamHistory()
            {
                TeamId = selectedTeam.Id,
                EventTime = selectedTeam.ModifiedOn,
                EventBody = "Team description updated to {0} by {1}."
            };
            _vwsDbContext.AddTeamHistory(newHistory);
            _vwsDbContext.Save();

            var user = await _vwsDbContext.GetUserProfileAsync(userId);
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.Text,
                Body = selectedTeam.Name,
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.User,
                Body = JsonConvert.SerializeObject(new UserModel()
                {
                    NickName = user.NickName,
                    ProfileImageGuid = user.ProfileImageGuid,
                    UserId = user.UserId
                }),
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            #endregion

            var users = (await _teamManager.GetTeamMembers(id)).Select(user => user.UserId).ToList();
            users = users.Distinct().ToList();
            users.Remove(LoggedInUserId.Value);
            string emailMessage = "<b>«{0}»</b> updated description from <b>«{1}»</b> to <b>«{2}»</b> in your team with name <b>«{3}»</b>.";
            string[] arguments = { LoggedInNickName, lastDescription, selectedTeam.Description, selectedTeam.Name };
            await _notificationService.SendMultipleEmails((int)EmailTemplateEnum.NotificationEmail, users, emailMessage, "Team Update", arguments);

            _notificationService.SendMultipleNotification(users, (byte)SeedDataEnum.NotificationTypes.Team, newHistory.Id);

            response.Message = "Team description updated successfully!";
            return Ok(response);
        }

        [HttpPut]
        [Authorize]
        [Route("updateTeamColor")]
        public async Task<IActionResult> UpdateTeamColor(int id, [FromBody] StringModel model)
        {
            string newColor = model.Value;
            var response = new ResponseModel();
            Guid userId = LoggedInUserId.Value;

            if (!String.IsNullOrEmpty(newColor) && newColor.Length > 6)
            {
                response.Message = "Team model data has problem.";
                response.AddError(_localizer["Length of color is more than 6 characters."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var selectedTeam = await _vwsDbContext.GetTeamAsync(id);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.Message = "Team not found";
                response.AddError(_localizer["There is no team with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }
            if (!_permissionService.HasAccessToTeam(userId, id))
            {
                response.Message = "Access team is forbidden";
                response.AddError(_localizer["You are not a member of team."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (selectedTeam.Color == newColor)
            {
                response.Message = "Duplicate data";
                return Ok(response);
            }

            var lastColor = selectedTeam.Color;

            selectedTeam.ModifiedBy = userId;
            selectedTeam.ModifiedOn = DateTime.UtcNow;
            selectedTeam.Color = newColor;
            _vwsDbContext.Save();

            #region History
            var newHistory = new TeamHistory()
            {
                TeamId = selectedTeam.Id,
                EventTime = selectedTeam.ModifiedOn,
                EventBody = "Team color updated from {0} to {1} by {2}."
            };
            _vwsDbContext.AddTeamHistory(newHistory);
            _vwsDbContext.Save();

            var user = await _vwsDbContext.GetUserProfileAsync(userId);
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.Color,
                Body = lastColor,
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.Color,
                Body = selectedTeam.Color,
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.User,
                Body = JsonConvert.SerializeObject(new UserModel()
                {
                    NickName = user.NickName,
                    ProfileImageGuid = user.ProfileImageGuid,
                    UserId = user.UserId
                }),
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            #endregion

            var users = (await _teamManager.GetTeamMembers(id)).Select(user => user.UserId).ToList();
            users = users.Distinct().ToList();
            users.Remove(LoggedInUserId.Value);
            string emailMessage = "<b>«{0}»</b> updated color from <b>«{1}»</b> to <b>«{2}»</b> in your team with name <b>«{3}»</b>.";
            string[] arguments = { LoggedInNickName, lastColor, selectedTeam.Color, selectedTeam.Name };
            await _notificationService.SendMultipleEmails((int)EmailTemplateEnum.NotificationEmail, users, emailMessage, "Team Update", arguments);

            _notificationService.SendMultipleNotification(users, (byte)SeedDataEnum.NotificationTypes.Team, newHistory.Id);

            response.Message = "Team color updated successfully!";
            return Ok(response);
        }

        [HttpPut]
        [Authorize]
        [Route("uploadTeamImage")]
        public async Task<IActionResult> UploadTeamImage(IFormFile image, int id)
        {
            var response = new ResponseModel<Guid>();

            string[] types = { "png", "jpg", "jpeg" };

            var files = Request.Form.Files.ToList();

            Guid userId = LoggedInUserId.Value;

            if (files.Count > 1)
            {
                response.AddError(_localizer["There is more than one file."]);
                response.Message = "Too many files passed";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }
            if (files.Count == 0 && image == null)
            {
                response.AddError(_localizer["You did not upload an image."]);
                response.Message = "There is no image";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }
            var uploadedImage = files.Count == 0 ? image : files[0];

            var selectedTeam = await _vwsDbContext.GetTeamAsync(id);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.AddError(_localizer["There is no team with given Id."]);
                response.Message = "Team not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var selectedTeamMember = await _vwsDbContext.GetTeamMemberAsync(id, userId);
            if (selectedTeamMember == null)
            {
                response.AddError(_localizer["You are not a member of team."]);
                response.Message = "Not member of team";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            if (!_imageService.IsImage(uploadedImage))
            {
                response.AddError(_localizer["Invalid file."]);
                response.Message = "Invalid file";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_imageService.IsImageSquare(uploadedImage))
            {
                response.AddError(_localizer["Image must be square."]);
                response.Message = "Invalid aspect ratio";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            ResponseModel<File> fileResponse;

            if (selectedTeam.TeamImage != null)
            {
                fileResponse = await _fileManager.WriteFile(uploadedImage, userId, "profileImages", (int)selectedTeam.TeamImageId, types.ToList());
                if (fileResponse.HasError)
                {
                    foreach (var error in fileResponse.Errors)
                        response.AddError(_localizer[error]);
                    response.Message = "Error in writing file";
                    return StatusCode(StatusCodes.Status500InternalServerError, response);
                }
                selectedTeam.TeamImage.RecentFileId = fileResponse.Value.Id;
            }
            else
            {
                var time = DateTime.UtcNow;
                var newFileContainer = new FileContainer
                {
                    ModifiedOn = time,
                    CreatedOn = time,
                    CreatedBy = userId,
                    ModifiedBy = userId,
                    Guid = Guid.NewGuid()
                };
                await _vwsDbContext.AddFileContainerAsync(newFileContainer);
                _vwsDbContext.Save();
                fileResponse = await _fileManager.WriteFile(uploadedImage, userId, "profileImages", newFileContainer.Id, types.ToList());
                if (fileResponse.HasError)
                {
                    foreach (var error in fileResponse.Errors)
                        response.AddError(_localizer[error]);
                    response.Message = "Error in writing file";
                    _vwsDbContext.DeleteFileContainer(newFileContainer);
                    _vwsDbContext.Save();
                    return StatusCode(StatusCodes.Status500InternalServerError, response);
                }
                newFileContainer.RecentFileId = fileResponse.Value.Id;
                selectedTeam.TeamImageId = newFileContainer.Id;
                selectedTeam.TeamImageGuid = newFileContainer.Guid;
            }
            selectedTeam.ModifiedBy = LoggedInUserId.Value;
            selectedTeam.ModifiedOn = DateTime.UtcNow;
            _vwsDbContext.Save();

            #region History
            var newHistory = new TeamHistory()
            {
                TeamId = selectedTeam.Id,
                EventTime = selectedTeam.ModifiedOn,
                EventBody = "Team image updated to {0} by {1}."
            };
            _vwsDbContext.AddTeamHistory(newHistory);
            _vwsDbContext.Save();

            var user = await _vwsDbContext.GetUserProfileAsync(userId);
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.File,
                Body = JsonConvert.SerializeObject(new FileModel()
                {
                    Extension = fileResponse.Value.Extension,
                    FileContainerGuid = fileResponse.Value.FileContainerGuid,
                    Name = fileResponse.Value.Name,
                    Size = fileResponse.Value.Size
                }),
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.User,
                Body = JsonConvert.SerializeObject(new UserModel()
                {
                    NickName = user.NickName,
                    ProfileImageGuid = user.ProfileImageGuid,
                    UserId = user.UserId
                }),
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            #endregion

            var users = (await _teamManager.GetTeamMembers(id)).Select(user => user.UserId).ToList();
            users = users.Distinct().ToList();
            users.Remove(LoggedInUserId.Value);
            string emailMessage = "<b>«{0}»</b> updated team image to <b>«{1}»</b> in your team with name <b>«{2}»</b>.";
            string[] arguments = { LoggedInNickName, $"<a href='{Request.Scheme}://{Request.Host}/en-US/File/get?id={fileResponse.Value.FileContainerGuid}'>{fileResponse.Value.Name}</a>", selectedTeam.Name };
            await _notificationService.SendMultipleEmails((int)EmailTemplateEnum.NotificationEmail, users, emailMessage, "Team Update", arguments);

            _notificationService.SendMultipleNotification(users, (byte)SeedDataEnum.NotificationTypes.Team, newHistory.Id);

            _imageService.SaveInOtherQualities(fileResponse.Value);

            response.Value = fileResponse.Value.FileContainerGuid;
            response.Message = "Team image added successfully!";
            return Ok(response);
        }

        [HttpGet]
        [Authorize]
        [Route("isNameOfGroupUsed")]
        public bool IsNameOfGroupUsed(string name)
        {
            Guid userId = LoggedInUserId.Value;

            return _vwsDbContext.TeamMembers.Any(teamMember => teamMember.UserProfileId == userId &&
                                                teamMember.Team.Name == name &&
                                                teamMember.Team.IsDeleted == false &&
                                                teamMember.IsDeleted == false);
        }

        [HttpGet]
        [Authorize]
        [Route("getAll")]
        public  IEnumerable<TeamExcludingUsersAndDepartmentsResponseModel> GetAllTeams()
        {
            Guid userId = LoggedInUserId.Value;

            _logger.LogInformation("User IP is : " + Request.HttpContext.Connection.RemoteIpAddress.ToString());
            _logger.LogInformation("User Browser is : " + Request.Headers["User-Agent"].ToString());

            List<TeamExcludingUsersAndDepartmentsResponseModel> response = new List<TeamExcludingUsersAndDepartmentsResponseModel>();

            var userTeamOrders = _vwsDbContext.UserTeamOrders.Include(userTeamOrder => userTeamOrder.Team)
                                                             .Where(userTeamOrder => userTeamOrder.UserProfileId == userId)
                                                             .ToList();

            var userTeams = _teamManager.GetAllUserTeams(userId);

            var validTeamOrdersTeams = userTeamOrders.Where(userTeamOrder => userTeams.Contains(userTeamOrder.Team))
                                                     .OrderBy(userTeamOrder => userTeamOrder.Order)
                                                     .Select(userTeamOrder => userTeamOrder.Team)
                                                     .ToList();

            var userTeamsNotIncluded = validTeamOrdersTeams.Count == userTeams.Count ? new List<Team>() : userTeams.Except(validTeamOrdersTeams);

            foreach (var validTeamOrderTeam in validTeamOrdersTeams)
            {
                response.Add(new TeamExcludingUsersAndDepartmentsResponseModel()
                {
                    Id = validTeamOrderTeam.Id,
                    TeamTypeId = validTeamOrderTeam.TeamTypeId,
                    Name = validTeamOrderTeam.Name,
                    Description = validTeamOrderTeam.Description,
                    Color = validTeamOrderTeam.Color,
                    CreatedBy = _userService.GetUser(validTeamOrderTeam.CreatedBy),
                    ModifiedBy = _userService.GetUser(validTeamOrderTeam.ModifiedBy),
                    CreatedOn = validTeamOrderTeam.CreatedOn,
                    ModifiedOn = validTeamOrderTeam.ModifiedOn,
                    Guid = validTeamOrderTeam.Guid,
                    TeamImageGuid = validTeamOrderTeam.TeamImageGuid,
                    NumberOfDepartments = _vwsDbContext.Departments.Where(department => department.TeamId == validTeamOrderTeam.Id && !department.IsDeleted).Count(),
                    NumberOfMembers = _vwsDbContext.TeamMembers.Where(teamMember => teamMember.TeamId == validTeamOrderTeam.Id && !teamMember.IsDeleted).Count(),
                    NumberOfTasks = _vwsDbContext.GeneralTasks.Where(task => task.TeamId == validTeamOrderTeam.Id && !task.IsDeleted).Count(),
                    NumberOfProjects = _vwsDbContext.Projects.Where(project => project.TeamId == validTeamOrderTeam.Id && !project.IsDeleted).Count(),
                });
            }
            foreach (var userTeamNotIncluded in userTeamsNotIncluded)
            {
                response.Add(new TeamExcludingUsersAndDepartmentsResponseModel()
                {
                    Id = userTeamNotIncluded.Id,
                    TeamTypeId = userTeamNotIncluded.TeamTypeId,
                    Name = userTeamNotIncluded.Name,
                    Description = userTeamNotIncluded.Description,
                    Color = userTeamNotIncluded.Color,
                    CreatedBy = _userService.GetUser(userTeamNotIncluded.CreatedBy),
                    ModifiedBy = _userService.GetUser(userTeamNotIncluded.ModifiedBy),
                    CreatedOn = userTeamNotIncluded.CreatedOn,
                    ModifiedOn = userTeamNotIncluded.ModifiedOn,
                    Guid = userTeamNotIncluded.Guid,
                    TeamImageGuid = userTeamNotIncluded.TeamImageGuid,
                    NumberOfDepartments = _vwsDbContext.Departments.Where(department => department.TeamId == userTeamNotIncluded.Id && !department.IsDeleted).Count(),
                    NumberOfMembers = _vwsDbContext.TeamMembers.Where(teamMember => teamMember.TeamId == userTeamNotIncluded.Id && !teamMember.IsDeleted).Count(),
                    NumberOfTasks = _vwsDbContext.GeneralTasks.Where(task => task.TeamId == userTeamNotIncluded.Id && !task.IsDeleted).Count(),
                    NumberOfProjects = _vwsDbContext.Projects.Where(project => project.TeamId == userTeamNotIncluded.Id && !project.IsDeleted).Count(),
                });
            }

            return response;
        }

        [HttpGet]
        [Authorize]
        [Route("get")]
        public async Task<IActionResult> GetTeam(int id)
        {
            Guid userId = LoggedInUserId.Value;

            var response = new ResponseModel<TeamResponseModel>();

            var selectedTeam = await _vwsDbContext.GetTeamAsync(id);

            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.AddError(_localizer["There is no team with given Id."]);
                response.Message = "Team not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_permissionService.HasAccessToTeam(userId, id))
            {
                response.AddError(_localizer["You are not a member of team."]);
                response.Message = "Team access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            response.Value = new TeamResponseModel()
            {
                Id = selectedTeam.Id,
                TeamTypeId = selectedTeam.TeamTypeId,
                Name = selectedTeam.Name,
                Description = selectedTeam.Description,
                Color = selectedTeam.Color,
                CreatedBy = _userService.GetUser(selectedTeam.CreatedBy),
                ModifiedBy = _userService.GetUser(selectedTeam.ModifiedBy),
                Guid = selectedTeam.Guid,
                CreatedOn = selectedTeam.CreatedOn,
                ModifiedOn = selectedTeam.ModifiedOn,
                TeamImageGuid = selectedTeam.TeamImageGuid,
                NumberOfDepartments = _vwsDbContext.Departments.Where(department => department.TeamId == selectedTeam.Id && !department.IsDeleted).Count(),
                NumberOfMembers = _vwsDbContext.TeamMembers.Where(teamMember => teamMember.TeamId == selectedTeam.Id && !teamMember.IsDeleted).Count(),
                NumberOfTasks = _vwsDbContext.GeneralTasks.Where(task => task.TeamId == selectedTeam.Id && !task.IsDeleted).Count(),
                NumberOfProjects = _vwsDbContext.Projects.Where(project => project.TeamId == selectedTeam.Id && !project.IsDeleted).Count(),
                Users = await _teamManager.GetTeamMembers(selectedTeam.Id),
                Departments = await GetDepartments(selectedTeam.Id)
            };
            response.Message = "Team retured successfully!";
            return Ok(response);
        }

        [HttpGet]
        [Authorize]
        [Route("getNumberOfTeams")]
        public int GetNumberOfTeams()
        {
            Guid userId = LoggedInUserId.Value;

            return _teamManager.GetAllUserTeams(userId).Count;
        }

        [HttpGet]
        [Authorize]
        [Route("getDepartments")]
        public async Task<IActionResult> GetTeamDepartments(int id)
        {
            var response = new ResponseModel<List<DepartmentResponseModel>>();
            var departments = new List<DepartmentResponseModel>();
            var userId = LoggedInUserId.Value;

            var selectedTeam = await _vwsDbContext.GetTeamAsync(id);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.Message = "Team not found";
                response.AddError(_localizer["There is no team with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_permissionService.HasAccessToTeam(userId, id))
            {
                response.Message = "Access team is forbidden";
                response.AddError(_localizer["You are not a member of team."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var teamDepartments = _vwsDbContext.Departments.Where(department => department.TeamId == id && !department.IsDeleted).OrderByDescending(department => department.CreatedOn);

            foreach (var teamDepartment in teamDepartments)
                departments.Add(new DepartmentResponseModel()
                {
                    Id = teamDepartment.Id,
                    Name = teamDepartment.Name,
                    DepartmentImageGuid = teamDepartment.DepartmentImageGuid,
                    Description = teamDepartment.Description,
                    Color = teamDepartment.Color,
                    CreatedBy = (await _vwsDbContext.GetUserProfileAsync(teamDepartment.CreatedBy)).NickName,
                    CreatedOn = teamDepartment.CreatedOn,
                    Guid = teamDepartment.Guid,
                    ModifiedBy = (await _vwsDbContext.GetUserProfileAsync(teamDepartment.ModifiedBy)).NickName,
                    ModifiedOn = teamDepartment.ModifiedOn,
                    TeamId = teamDepartment.TeamId,
                    Users = await _departmentManager.GetDepartmentMembers(teamDepartment.Id)
                });

            response.Value = departments;
            response.Message = "Team departments returned successfully!";
            return Ok(response);
        }

        [HttpGet]
        [Authorize]
        [Route("getProjects")]
        public async Task<IActionResult> GetTeamProjects(int id)
        {
            var response = new ResponseModel<List<ProjectResponseModel>>();
            var projects = new List<ProjectResponseModel>();
            var userId = LoggedInUserId.Value;

            var selectedTeam = await _vwsDbContext.GetTeamAsync(id);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.Message = "Team not found";
                response.AddError(_localizer["There is no team with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_permissionService.HasAccessToTeam(userId, id))
            {
                response.Message = "Access team is forbidden";
                response.AddError(_localizer["You are not a member of team."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var teamProjects = _vwsDbContext.Projects.Where(project => project.TeamId == id && !project.IsDeleted).OrderByDescending(project => project.CreatedOn);
            foreach (var teamProject in teamProjects)
                projects.Add(new ProjectResponseModel()
                {
                    Id = teamProject.Id,
                    Description = teamProject.Description,
                    Color = teamProject.Color,
                    EndDate = teamProject.EndDate,
                    Guid = teamProject.Guid,
                    Name = teamProject.Name,
                    StartDate = teamProject.StartDate,
                    StatusId = teamProject.StatusId,
                    TeamId = teamProject.TeamId,
                    TeamName = teamProject.TeamId == null ? null : _vwsDbContext.Teams.FirstOrDefault(team => team.Id == teamProject.TeamId).Name,
                    ProjectImageGuid = teamProject.ProjectImageGuid,
                    DepartmentIds = teamProject.ProjectDepartments.Select(projectDepartment => projectDepartment.DepartmentId).ToList(),
                    NumberOfUpdates = _vwsDbContext.ProjectHistories.Where(history => history.ProjectId == teamProject.Id).Count(),
                    Users = _projectManager.GetProjectUsers(teamProject.Id),
                    NumberOfTasks = _projectManager.GetNumberOfProjectTasks(teamProject.Id),
                    SpentTimeInMinutes = _projectManager.GetProjectSpentTime(teamProject.Id),
                    CreatedBy = _userService.GetUser(teamProject.CreatedBy),
                    ModifiedBy = _userService.GetUser(teamProject.ModifiedBy),
                    CreatedOn = teamProject.CreatedOn,
                    ModifiedOn = teamProject.ModifiedOn
                });

            response.Value = projects;
            response.Message = "Team departments returned successfully!";
            return Ok(response);
        }

        [HttpGet]
        [Authorize]
        [Route("getTasks")]
        public async Task<IActionResult> GetTeamTasks(int id)
        {
            var response = new ResponseModel<List<TaskResponseModel>>();
            var tasks = new List<TaskResponseModel>();
            var userId = LoggedInUserId.Value;

            var selectedTeam = await _vwsDbContext.GetTeamAsync(id);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.Message = "Team not found";
                response.AddError(_localizer["There is no team with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_permissionService.HasAccessToTeam(userId, id))
            {
                response.Message = "Access team is forbidden";
                response.AddError(_localizer["You are not a member of team."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var teamTasks = _vwsDbContext.GeneralTasks.Where(task => task.TeamId == id && !task.IsDeleted).OrderByDescending(task => task.CreatedOn);
            foreach (var teamTask in teamTasks)
                tasks.Add(new TaskResponseModel()
                {
                    Id = teamTask.Id,
                    Title = teamTask.Title,
                    Description = teamTask.Description,
                    StartDate = teamTask.StartDate,
                    EndDate = teamTask.EndDate,
                    CreatedOn = teamTask.CreatedOn,
                    ModifiedOn = teamTask.ModifiedOn,
                    CreatedBy = _userService.GetUser(teamTask.CreatedBy),
                    ModifiedBy = _userService.GetUser(teamTask.ModifiedBy),
                    Guid = teamTask.Guid,
                    PriorityId = teamTask.TaskPriorityId,
                    PriorityTitle = _localizer[((SeedDataEnum.TaskPriority)teamTask.TaskPriorityId).ToString()],
                    UsersAssignedTo = _taskManager.GetAssignedTo(teamTask.Id),
                    ProjectId = teamTask.ProjectId,
                    TeamId = teamTask.TeamId,
                    TeamName = teamTask.TeamId == null ? null : _vwsDbContext.Teams.FirstOrDefault(team => team.Id == teamTask.TeamId).Name,
                    ProjectName = teamTask.ProjectId == null ? null : _vwsDbContext.Projects.FirstOrDefault(project => project.Id == teamTask.ProjectId).Name,
                    StatusId = teamTask.TaskStatusId,
                    StatusTitle = _vwsDbContext.TaskStatuses.FirstOrDefault(statuse => statuse.Id == teamTask.TaskStatusId).Title,
                    CheckLists = _taskManager.GetCheckLists(teamTask.Id),
                    Tags = _taskManager.GetTaskTags(teamTask.Id),
                    Comments = await _taskManager.GetTaskComments(teamTask.Id),
                    Attachments = _taskManager.GetTaskAttachments(teamTask.Id),
                    IsUrgent = teamTask.IsUrgent
                });

            response.Value = tasks;
            response.Message = "Team departments returned successfully!";
            return Ok(response);
        }

        [HttpGet]
        [Authorize]
        [Route("getTeamHistory")]
        public IActionResult GetTeamHistory(int id)
        {
            var userId = LoggedInUserId.Value;
            var response = new ResponseModel<List<HistoryModel>>();

            var selectedTeam = _vwsDbContext.Teams.FirstOrDefault(team => team.Id == id);

            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.Message = "Team not found";
                response.AddError(_localizer["There is no team with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_permissionService.HasAccessToTeam(userId, id))
            {
                response.Message = "Access team is forbidden";
                response.AddError(_localizer["You are not a member of team."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var events = new List<HistoryModel>();
            var teamEvents = _vwsDbContext.TeamHistories.Where(teamHistory => teamHistory.TeamId == id).OrderByDescending(teamHistory => teamHistory.EventTime);
            foreach (var teamEvent in teamEvents)
            {
                var parameters = _vwsDbContext.TeamHistoryParameters.Where(param => param.TeamHistoryId == teamEvent.Id)
                                                                    .OrderBy(param => param.Id)
                                                                    .ToList();
                for (int i = 0; i < parameters.Count(); i++)
                {
                    if (parameters[i].ActivityParameterTypeId == (byte)SeedDataEnum.ActivityParameterTypes.Text && parameters[i].ShouldBeLocalized)
                        parameters[i].Body = _localizer[parameters[i].Body];
                }
                events.Add(new HistoryModel()
                {
                    Message = _localizer[teamEvent.EventBody],
                    Parameters = parameters.Select(param => new HistoryParameterModel() { ParameterBody = param.Body, ParameterType = param.ActivityParameterTypeId }).ToList(),
                    Time = teamEvent.EventTime
                });
            }

            response.Message = "History returned successfully!";
            response.Value = events;
            return Ok(response);
        }

        [HttpDelete]
        [Authorize]
        [Route("delete")]
        public async Task<IActionResult> DeleteTeam(int id)
        {
            var response = new ResponseModel();

            Guid userId = LoggedInUserId.Value;

            var selectedTeam = await _vwsDbContext.GetTeamAsync(id);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.AddError(_localizer["There is no team with given Id."]);
                response.Message = "Team not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_permissionService.HasAccessToTeam(userId, id))
            {
                response.AddError(_localizer["You are not a member of team."]);
                response.Message = "Not member of team";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            if (userId != selectedTeam.CreatedBy)
            {
                response.AddError(_localizer["Teams can only get deleted by the creator."]);
                response.Message = "Delete team access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            var deletionTime = DateTime.UtcNow;

            selectedTeam.IsDeleted = true;
            selectedTeam.ModifiedBy = userId;
            selectedTeam.ModifiedOn = deletionTime;
            _vwsDbContext.Save();

            var teamProjects = _vwsDbContext.Projects.Where(project => project.TeamId == id &&
                                                                      !project.IsDeleted);

            var teamDepartments = _vwsDbContext.Departments.Where(department => department.TeamId == id &&
                                                                               !department.IsDeleted);

            foreach (var teamProject in teamProjects)
            {
                teamProject.IsDeleted = true;
                teamProject.ModifiedBy = userId;
                teamProject.ModifiedOn = deletionTime;
            }

            foreach (var teamDepartment in teamDepartments)
            {
                teamDepartment.IsDeleted = true;
                teamDepartment.ModifiedBy = userId;
                teamDepartment.ModifiedOn = deletionTime;
            }
            _vwsDbContext.Save();

            DeleteTeamTasks(selectedTeam.Id, selectedTeam.ModifiedOn);
            DeleteTeamProjects(selectedTeam.Id, selectedTeam.ModifiedOn);
            DeleteTeamDepartments(selectedTeam.Id, selectedTeam.ModifiedOn);
            DeleteTeamEvents(selectedTeam.Id, selectedTeam.ModifiedOn);

            var teamTasks = _vwsDbContext.GeneralTasks.Where(task => task.TeamId == selectedTeam.Id && !task.IsDeleted);
            foreach (var teamTask in teamTasks)
                _taskManager.StopRunningTimes(teamTask.Id, selectedTeam.ModifiedOn);

            #region History
            var newHistory = new TeamHistory()
            {
                TeamId = selectedTeam.Id,
                EventTime = selectedTeam.ModifiedOn,
                EventBody = "{0} deleted team."
            };
            _vwsDbContext.AddTeamHistory(newHistory);
            _vwsDbContext.Save();

            var user = await _vwsDbContext.GetUserProfileAsync(userId);
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.User,
                Body = JsonConvert.SerializeObject(new UserModel()
                {
                    NickName = user.NickName,
                    ProfileImageGuid = user.ProfileImageGuid,
                    UserId = user.UserId
                }),
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            #endregion

            var users = (await _teamManager.GetTeamMembers(id)).Select(user => user.UserId).ToList();
            users = users.Distinct().ToList();
            users.Remove(LoggedInUserId.Value);
            string emailMessage = "<b>«{0}»</b> deleted team <b>«{1}»</b>.";
            string[] arguments = { LoggedInNickName, selectedTeam.Name };
            await _notificationService.SendMultipleEmails((int)EmailTemplateEnum.NotificationEmail, users, emailMessage, "Team Update", arguments);

            _notificationService.SendMultipleNotification(users, (byte)SeedDataEnum.NotificationTypes.Team, newHistory.Id);

            response.Message = "Team deleted successfully!";
            return Ok(response);
        }
        #endregion

        #region InviteLinkAPIS
        [HttpPost]
        [Authorize]
        [Route("createInviteLink")]
        public async Task<IActionResult> CreateInviteLink(int id)
        {
            var response = new ResponseModel<TeamInviteLinkResponseModel>();

            var selectedTeam = await _vwsDbContext.GetTeamAsync(id);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.Message = "Team not found";
                response.AddError(_localizer["There is no team with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            Guid userId = LoggedInUserId.Value;

            if (_permissionService.HasAccessToTeam(userId, id))
            {
                response.Message = "You are not member of team";
                response.AddError(_localizer["You are not a member of team."]);
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            DateTime creationTime = DateTime.UtcNow;

            Guid inviteLinkGuid = Guid.NewGuid();

            var newInviteLink = new TeamInviteLink()
            {
                TeamId = id,
                CreatedBy = userId,
                ModifiedBy = userId,
                CreatedOn = creationTime,
                ModifiedOn = creationTime,
                LinkGuid = inviteLinkGuid,
                IsRevoked = false
            };

            await _vwsDbContext.AddTeamInviteLinkAsync(newInviteLink);
            _vwsDbContext.Save();

            #region History
            var newHistory = new TeamHistory()
            {
                TeamId = id,
                EventTime = creationTime,
                EventBody = "{0} created new invite link with id {1}."
            };
            _vwsDbContext.AddTeamHistory(newHistory);
            _vwsDbContext.Save();

            var user = await _vwsDbContext.GetUserProfileAsync(userId);
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.User,
                Body = JsonConvert.SerializeObject(new UserModel()
                {
                    NickName = user.NickName,
                    ProfileImageGuid = user.ProfileImageGuid,
                    UserId = user.UserId
                }),
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.Text,
                Body = newInviteLink.LinkGuid.ToString(),
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            #endregion

            var users = (await _teamManager.GetTeamMembers(id)).Select(user => user.UserId).ToList();
            users = users.Distinct().ToList();
            users.Remove(LoggedInUserId.Value);
            string emailMessage = "<b>«{0}»</b> created new invite link with id <b>«{1}»</b> for team <b>«{2}»</b>.";
            string[] arguments = { LoggedInNickName, newInviteLink.LinkGuid.ToString(), selectedTeam.Name };
            await _notificationService.SendMultipleEmails((int)EmailTemplateEnum.NotificationEmail, users, emailMessage, "Team Update", arguments);

            _notificationService.SendMultipleNotification(users, (byte)SeedDataEnum.NotificationTypes.Team, newHistory.Id);

            response.Value = new TeamInviteLinkResponseModel()
            {
                Id = newInviteLink.Id,
                TeamName = (await _vwsDbContext.GetTeamAsync(newInviteLink.TeamId)).Name,
                IsRevoked = newInviteLink.IsRevoked,
                LinkGuid = newInviteLink.LinkGuid.ToString(),
                CreatedBy = (await _vwsDbContext.GetUserProfileAsync(newInviteLink.CreatedBy)).NickName,
                ModifiedBy = (await _vwsDbContext.GetUserProfileAsync(newInviteLink.ModifiedBy)).NickName,
                CreatedOn = newInviteLink.CreatedOn,
                ModifiedOn = newInviteLink.ModifiedOn
            };

            response.Message = "Invite link created successfully!";
            return Ok(response);
        }

        [HttpPut]
        [Authorize]
        [Route("revokeLink")]
        public async Task<IActionResult> RevokeLink(int linkId)
        {
            var response = new ResponseModel();

            Guid userId = LoggedInUserId.Value;

            var selectedInviteLink = await _vwsDbContext.GetTeamInviteLinkByIdAsync(linkId);

            if (selectedInviteLink == null || selectedInviteLink.Team.IsDeleted || selectedInviteLink.IsRevoked)
            {
                response.Message = "Link not found";
                response.AddError(_localizer["Link does not exist."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }
            var selectedTeam = await _vwsDbContext.GetTeamAsync(selectedInviteLink.TeamId);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.Message = "Team not found";
                response.AddError(_localizer["There is no team with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }
            if (_permissionService.HasAccessToTeam(userId, selectedInviteLink.TeamId))
            {
                response.Message = "You are not member of team";
                response.AddError(_localizer["You are not a member of team."]);
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            selectedInviteLink.IsRevoked = true;
            selectedTeam.ModifiedBy = userId;
            selectedTeam.ModifiedOn = DateTime.UtcNow;
            _vwsDbContext.Save();

            #region History
            var newHistory = new TeamHistory()
            {
                TeamId = selectedInviteLink.TeamId,
                EventTime = selectedTeam.ModifiedOn,
                EventBody = "Invite link with id {0} revoked by {1}."
            };
            _vwsDbContext.AddTeamHistory(newHistory);
            _vwsDbContext.Save();

            var user = await _vwsDbContext.GetUserProfileAsync(userId);
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.Text,
                Body = selectedInviteLink.LinkGuid.ToString(),
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.User,
                Body = JsonConvert.SerializeObject(new UserModel()
                {
                    NickName = user.NickName,
                    ProfileImageGuid = user.ProfileImageGuid,
                    UserId = user.UserId
                }),
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            #endregion

            var users = (await _teamManager.GetTeamMembers(selectedTeam.Id)).Select(user => user.UserId).ToList();
            users = users.Distinct().ToList();
            users.Remove(LoggedInUserId.Value);
            string emailMessage = "<b>«{0}»</b> revoked invite link with id <b>«{1}»</b> in team <b>«{2}»</b>.";
            string[] arguments = { LoggedInNickName, selectedInviteLink.LinkGuid.ToString(), selectedTeam.Name };
            await _notificationService.SendMultipleEmails((int)EmailTemplateEnum.NotificationEmail, users, emailMessage, "Team Update", arguments);

            _notificationService.SendMultipleNotification(users, (byte)SeedDataEnum.NotificationTypes.Team, newHistory.Id);

            response.Message = "Team updated successfully!";
            return Ok(response);
        }

        [HttpGet]
        [Authorize]
        [Route("getLinks")]
        public async Task<IEnumerable<TeamInviteLinkResponseModel>> GetInviteLinks()
        {
            Guid userId = LoggedInUserId.Value;

            List<TeamInviteLinkResponseModel> response = new List<TeamInviteLinkResponseModel>();

            var userTeamInviteLinks = _vwsDbContext.TeamInviteLinks.Include(teamInviteLink => teamInviteLink.Team)
                                                                  .Where(teamInviteLink => teamInviteLink.CreatedBy == userId &&
                                                                            teamInviteLink.IsRevoked == false &&
                                                                            teamInviteLink.Team.IsDeleted == false);

            var teamMembers = _vwsDbContext.TeamMembers.Where(teamMemeber => teamMemeber.UserProfileId == userId && teamMemeber.IsDeleted == false);

            foreach (var userTeamInviteLink in userTeamInviteLinks)
            {
                if (teamMembers.Any(teamMember => teamMember.TeamId == userTeamInviteLink.TeamId))
                {
                    response.Add(new TeamInviteLinkResponseModel()
                    {
                        Id = userTeamInviteLink.Id,
                        TeamName = (await _vwsDbContext.GetTeamAsync(userTeamInviteLink.TeamId)).Name,
                        IsRevoked = userTeamInviteLink.IsRevoked,
                        LinkGuid = userTeamInviteLink.LinkGuid.ToString(),
                        CreatedBy = (await _vwsDbContext.GetUserProfileAsync(userTeamInviteLink.CreatedBy)).NickName,
                        ModifiedBy = (await _vwsDbContext.GetUserProfileAsync(userTeamInviteLink.ModifiedBy)).NickName,
                        CreatedOn = userTeamInviteLink.CreatedOn,
                        ModifiedOn = userTeamInviteLink.ModifiedOn
                    });
                }
            }
            return response;
        }
        #endregion

        #region TeamMemberAPIS
        [HttpPost]
        [Authorize]
        [Route("addMembersToTeam")]
        public async Task<IActionResult> AddMemebersToTeam([FromBody] AddMembersToTeamModel model)
        {
            var response = new ResponseModel();
            var userId = LoggedInUserId.Value;
            var actorUser = await _vwsDbContext.GetUserProfileAsync(userId);

            model.EmailsForInvite = model.EmailsForInvite.Distinct().ToList();
            model.Users = model.Users;

            #region CheckModel
            var selectedTeam = _vwsDbContext.Teams.FirstOrDefault(team => team.Id == model.TeamId);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.Message = "Team not found";
                response.AddError(_localizer["There is no team with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_permissionService.HasAccessToTeam(userId, selectedTeam.Id))
            {
                response.AddError(_localizer["You are not a member of team."]);
                response.Message = "Not member of team";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            foreach (var email in model.EmailsForInvite)
            {
                if (!_emailChecker.IsValid(email))
                {
                    response.AddError(_localizer["Invalid emails."]);
                    response.Message = "Invalid emails";
                    return StatusCode(StatusCodes.Status400BadRequest, response);
                }
            }

            foreach (var user in model.Users)
            {
                if (!_vwsDbContext.UserProfiles.Any(profile => profile.UserId == user))
                {
                    response.AddError(_localizer["Invalid users."]);
                    response.Message = "Invalid users";
                    return StatusCode(StatusCodes.Status400BadRequest, response);
                }
            }

            var teamMembers = _permissionService.GetUsersHaveAccessToTeam(selectedTeam.Id);
            var userTeammates = _teamManager.GetUserTeammates(LoggedInUserId.Value);
            var usersCanBeAddedToTeam = userTeammates.Except(teamMembers).ToList();

            if (usersCanBeAddedToTeam.Intersect(model.Users).Count() != model.Users.Count)
            {
                response.AddError(_localizer["Invalid users."]);
                response.Message = "Invalid users";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            List<string> teamMemberEmails = new List<string>();
            foreach (var teamMember in teamMembers)
                teamMemberEmails.Add((await _userManager.FindByIdAsync(teamMember.ToString())).Email);

            model.EmailsForInvite = model.EmailsForInvite.Except(teamMemberEmails).ToList();
            #endregion

            var addTime = DateTime.UtcNow;
            foreach (var user in model.Users)
            {
                var newTeamMember = new TeamMember()
                {
                    CreatedOn = addTime,
                    IsDeleted = false,
                    TeamId = selectedTeam.Id,
                    UserProfileId = user
                };
                await _vwsDbContext.AddTeamMemberAsync(newTeamMember);
                _vwsDbContext.AddUsersActivity(new UsersActivity() { Time = addTime, TargetUserId = user, OwnerUserId = LoggedInUserId.Value });
                _vwsDbContext.Save();

                var addedUser = await _vwsDbContext.GetUserProfileAsync(user);

                #region HistoryAndNotif
                var newHistory = new TeamHistory()
                {
                    TeamId = selectedTeam.Id,
                    EventTime = newTeamMember.CreatedOn,
                    EventBody = "{0} added {1} to team."
                };
                _vwsDbContext.AddTeamHistory(newHistory);
                _vwsDbContext.Save();

                _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
                {
                    ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.User,
                    Body = JsonConvert.SerializeObject(new UserModel()
                    {
                        NickName = actorUser.NickName,
                        ProfileImageGuid = actorUser.ProfileImageGuid,
                        UserId = actorUser.UserId
                    }),
                    TeamHistoryId = newHistory.Id
                });
                _vwsDbContext.Save();
                _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
                {
                    ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.User,
                    Body = JsonConvert.SerializeObject(new UserModel()
                    {
                        NickName = addedUser.NickName,
                        ProfileImageGuid = addedUser.ProfileImageGuid,
                        UserId = addedUser.UserId
                    }),
                    TeamHistoryId = newHistory.Id
                });
                _vwsDbContext.Save();

                string[] args = { LoggedInNickName, selectedTeam.Name };
                _notificationService.SendSingleEmail((int)EmailTemplateEnum.NotificationEmail, "<b>«{0}»</b> added you to team <b>«{1}»</b>.", "New Team", addedUser.UserId, args);

                var users = (await _teamManager.GetTeamMembers(selectedTeam.Id)).Select(user => user.UserId).ToList();
                users = users.Distinct().ToList();
                users.Remove(LoggedInUserId.Value);

                _notificationService.SendMultipleNotification(users, (byte)SeedDataEnum.NotificationTypes.Team, newHistory.Id);

                users.Remove(addedUser.UserId);
                string emailMessage = "<b>«{0}»</b> added <b>«{1}»</b> to team <b>«{2}»</b>.";
                string[] arguments = { LoggedInNickName, addedUser.NickName, selectedTeam.Name };
                await _notificationService.SendMultipleEmails((int)EmailTemplateEnum.NotificationEmail, users, emailMessage, "Team Update", arguments);
                #endregion
            }

            await SendJoinTeamInvitaionLinks(model.EmailsForInvite, selectedTeam.Id);

            response.Message = "Uses added successfully!";
            return Ok(response);
        }

        [HttpPost]
        [Authorize]
        [Route("join")]
        public async Task<IActionResult> JoinTeam(string guid)
        {
            var response = new ResponseModel();

            Guid linkGuid = new Guid(guid);

            Guid userId = LoggedInUserId.Value;

            var selectedTeamLink = await _vwsDbContext.GetTeamInviteLinkByLinkGuidAsync(linkGuid);

            if (selectedTeamLink == null || selectedTeamLink.Team.IsDeleted || selectedTeamLink.IsRevoked)
            {
                response.Message = "Invalid link";
                response.AddError(_localizer["Link is not valid."]);
                return StatusCode(StatusCodes.Status406NotAcceptable, response);
            }
            var selectedTeam = await _vwsDbContext.GetTeamAsync(selectedTeamLink.TeamId);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.Message = "Team not found";
                response.AddError(_localizer["There is no team with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }
            if (_permissionService.HasAccessToTeam(userId, selectedTeam.Id))
            {
                response.Message = "User already joined";
                response.AddError(_localizer["You are already joined the team."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var newTeamMember = new TeamMember()
            {
                TeamId = selectedTeamLink.TeamId,
                CreatedOn = DateTime.UtcNow,
                UserProfileId = userId
            };
            await _vwsDbContext.AddTeamMemberAsync(newTeamMember);
            _vwsDbContext.Save();

            #region History
            var newHistory = new TeamHistory()
            {
                TeamId = newTeamMember.TeamId,
                EventTime = newTeamMember.CreatedOn,
                EventBody = "{0} joined the team using invite link."
            };
            _vwsDbContext.AddTeamHistory(newHistory);
            _vwsDbContext.Save();

            var user = await _vwsDbContext.GetUserProfileAsync(userId);
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.User,
                Body = JsonConvert.SerializeObject(new UserModel()
                {
                    NickName = user.NickName,
                    ProfileImageGuid = user.ProfileImageGuid,
                    UserId = user.UserId
                }),
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            #endregion

            var users = (await _teamManager.GetTeamMembers(selectedTeam.Id)).Select(user => user.UserId).ToList();
            users = users.Distinct().ToList();
            users.Remove(LoggedInUserId.Value);
            string emailMessage = "<b>«{0}»</b> joined team <b>«{1}»</b> using invite link.";
            string[] arguments = { LoggedInNickName, selectedTeam.Name };
            await _notificationService.SendMultipleEmails((int)EmailTemplateEnum.NotificationEmail, users, emailMessage, "Team Update", arguments);

            _notificationService.SendMultipleNotification(users, (byte)SeedDataEnum.NotificationTypes.Team, newHistory.Id);

            response.Message = "User added to team successfully!";
            return Ok(response);
        }

        [HttpDelete]
        [Authorize]
        [Route("deleteTeamMember")]
        public async Task<IActionResult> DeleteTeamMember(int id, Guid userId)
        {
            var response = new ResponseModel();

            var selectedTeam = _vwsDbContext.Teams.FirstOrDefault(team => team.Id == id);
            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.Message = "Team not found";
                response.AddError(_localizer["There is no team with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_permissionService.HasAccessToTeam(userId, selectedTeam.Id))
            {
                response.AddError(_localizer["You are not a member of team."]);
                response.Message = "Not member of team";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            var selectedTeamMemeber = _vwsDbContext.TeamMembers.FirstOrDefault(teamMemeber => teamMemeber.UserProfileId == userId && teamMemeber.TeamId == id && !teamMemeber.IsDeleted);
            if (selectedTeamMemeber == null)
            {
                response.Message = "Member not found";
                response.AddError(_localizer["Member not found."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if ((selectedTeam.CreatedBy == LoggedInUserId.Value && userId == LoggedInUserId.Value) ||
                (selectedTeam.CreatedBy != LoggedInUserId.Value && userId != LoggedInUserId.Value))
            {
                response.AddError(_localizer["You do not have permission to do this member removal."]);
                response.Message = "Invalid memeber removal";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            selectedTeamMemeber.IsDeleted = true;
            selectedTeamMemeber.DeletedOn = DateTime.UtcNow;
            _vwsDbContext.Save();

            var deletedUser = await _vwsDbContext.GetUserProfileAsync(userId);
            var actorUser = await _vwsDbContext.GetUserProfileAsync(LoggedInUserId.Value);

            #region History
            var newHistory = new TeamHistory()
            {
                TeamId = selectedTeam.Id,
                EventTime = selectedTeamMemeber.DeletedOn.Value,
                EventBody = "{0} removed {1} from team."
            };
            _vwsDbContext.AddTeamHistory(newHistory);
            _vwsDbContext.Save();
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.User,
                Body = JsonConvert.SerializeObject(new UserModel()
                {
                    NickName = actorUser.NickName,
                    ProfileImageGuid = actorUser.ProfileImageGuid,
                    UserId = actorUser.UserId
                }),
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            _vwsDbContext.AddTeamHistoryParameter(new TeamHistoryParameter()
            {
                ActivityParameterTypeId = (byte)SeedDataEnum.ActivityParameterTypes.User,
                Body = JsonConvert.SerializeObject(new UserModel()
                {
                    NickName = deletedUser.NickName,
                    ProfileImageGuid = deletedUser.ProfileImageGuid,
                    UserId = deletedUser.UserId
                }),
                TeamHistoryId = newHistory.Id
            });
            _vwsDbContext.Save();
            #endregion

            string[] args = { LoggedInNickName, selectedTeam.Name };
            await _notificationService.SendSingleEmail((int)EmailTemplateEnum.NotificationEmail, "<b>«{0}»</b> removed you from team <b>«{1}»</b>.", "Team Update", deletedUser.UserId, args);

            var users = (await _teamManager.GetTeamMembers(selectedTeam.Id)).Select(user => user.UserId).ToList();
            users = users.Distinct().ToList();
            users.Remove(LoggedInUserId.Value);
            users.Remove(userId);
            string emailMessage = "<b>«{0}»</b> removed <b>«{1}»</b> from team <b>«{2}»</b>.";
            string[] arguments = { LoggedInNickName, deletedUser.NickName, selectedTeam.Name };
            await _notificationService.SendMultipleEmails((int)EmailTemplateEnum.NotificationEmail, users, emailMessage, "Team Update", arguments);

            users.Add(userId);
            _notificationService.SendMultipleNotification(users, (byte)SeedDataEnum.NotificationTypes.Team, newHistory.Id);

            response.Message = "User deleted successfully!";
            return Ok(response);
        }
        #endregion

        #region TeammateAPIS
        [HttpGet]
        [Authorize]
        [Route("getTeammates")]
        public async Task<IActionResult> GetTeammates(int id)
        {
            var response = new ResponseModel<List<UserModel>>();
            var teammatesList = new List<UserModel>();

            var selectedTeam = await _vwsDbContext.GetTeamAsync(id);
            var userId = LoggedInUserId.Value;

            if (selectedTeam == null || selectedTeam.IsDeleted)
            {
                response.Message = "Team not found";
                response.AddError(_localizer["There is no team with given Id."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_permissionService.HasAccessToTeam(userId, id))
            {
                response.Message = "Access team is forbidden";
                response.AddError(_localizer["You are not a member of team."]);
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            List<UserProfile> userTeamMates = _vwsDbContext.TeamMembers
                .Include(teamMember => teamMember.UserProfile)
                .Where(teamMember => teamMember.TeamId == id && teamMember.IsDeleted == false)
                .Select(teamMember => teamMember.UserProfile).Distinct().ToList();

            foreach (var teamMate in userTeamMates)
            {
                UserProfile userProfile = await _vwsDbContext.GetUserProfileAsync(teamMate.UserId);
                teammatesList.Add(new UserModel()
                {
                    UserId = teamMate.UserId,
                    NickName = userProfile.NickName,
                    ProfileImageGuid = userProfile.ProfileImageGuid
                });
            }

            response.Message = "Team mates are given successfully!";
            response.Value = teammatesList;
            return Ok(response);
        }

        [HttpGet]
        [Authorize]
        [Route("getAllTeamMates")]
        public async Task<ICollection<UserModel>> GetAllTeamMates()
        {
            var result = new List<UserModel>();

            var userTeamMates = _teamManager.GetUserTeammates(LoggedInUserId.Value);
            var usersOrders = _vwsDbContext.UsersOrders.Where(userOrder => userOrder.OwnerUserId == LoggedInUserId.Value).ToList();

            var validUsersFromUsersOrders = usersOrders.Where(usersOrder => userTeamMates.Contains(usersOrder.TargetUserId))
                                                       .OrderBy(usersOrder => usersOrder.Order)
                                                       .Select(usersOrder => usersOrder.TargetUserId)
                                                       .ToList();

            var usersNotIncluded = validUsersFromUsersOrders.Count == userTeamMates.Count ? new List<Guid>() : userTeamMates.Except(validUsersFromUsersOrders);

            foreach (var userId in validUsersFromUsersOrders)
            {
                var user = await _userManager.FindByIdAsync(userId.ToString());
                var userProfile = await _vwsDbContext.GetUserProfileAsync(userId);
                result.Add(new UserModel()
                {
                    UserId = userId,
                    ProfileImageGuid = userProfile.ProfileImageGuid,
                    NickName = userProfile.NickName
                });
            }
            foreach (var userId in usersNotIncluded)
            {
                var user = await _userManager.FindByIdAsync(userId.ToString());
                var userProfile = await _vwsDbContext.GetUserProfileAsync(userId);
                result.Add(new UserModel()
                {
                    UserId = userId,
                    ProfileImageGuid = userProfile.ProfileImageGuid,
                    NickName = userProfile.NickName
                });
            }

            return result;
        }
        #endregion
    }
}
