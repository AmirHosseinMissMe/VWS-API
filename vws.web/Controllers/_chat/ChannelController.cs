﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using vws.web.Domain;
using vws.web.Domain._base;
using vws.web.Domain._chat;
using vws.web.Enums;
using vws.web.Models;
using vws.web.Models._chat;
using vws.web.Services._chat;

namespace vws.web.Controllers._chat
{
    [Route("{culture:culture}/[controller]")]
    [ApiController]
    public class ChannelController : BaseController
    {
        #region Feilds
        private readonly IStringLocalizer<ChannelController> _localizer;
        private readonly IVWS_DbContext _vwsDbContext;
        private readonly IChannelService _channelService;
        #endregion

        #region Ctor
        public ChannelController(IStringLocalizer<ChannelController> localizer,
                                 IVWS_DbContext vwsDbContext, IChannelService channelService)
        {
            _localizer = localizer;
            _vwsDbContext = vwsDbContext;
            _channelService = channelService;
        }
        #endregion

        #region PrivateMethods
        private void SetChannelsIsMuted(ref List<ChannelResponseModel> channelResponseModels)
        {
            var userId = LoggedInUserId.Value;

            foreach (var channelResponseModel in channelResponseModels)
            {
                var mutedChannel = _vwsDbContext.MutedChannels.FirstOrDefault(mChannel => mChannel.ChannelTypeId == channelResponseModel.ChannelTypeId &&
                                                                                    mChannel.ChannelId == channelResponseModel.Guid &&
                                                                                    mChannel.UserId == userId);

                if (mutedChannel != null && mutedChannel.IsMuted)
                {
                    if (mutedChannel.ForEver || mutedChannel.MuteUntil >= DateTime.UtcNow)
                        channelResponseModel.IsMuted = true;
                    else
                        mutedChannel.IsMuted = false;
                }
            }

            _vwsDbContext.Save();
        }

        private void SetChannelIsPinned(ref List<ChannelResponseModel> channelResponseModels)
        {
            var userId = LoggedInUserId.Value;

            foreach (var channelResponseModel in channelResponseModels)
            {
                var pinnedChannel = _vwsDbContext.PinnedChannels.FirstOrDefault(pChannel => pChannel.ChannelTypeId == channelResponseModel.ChannelTypeId &&
                                                                                           pChannel.ChannelId == channelResponseModel.Guid &&
                                                                                           pChannel.UserId == userId);

                if (pinnedChannel != null)
                {
                    channelResponseModel.EvenOrder = pinnedChannel.EvenOrder;
                    channelResponseModel.IsPinned = true;
                }
            }
        }

        private DateTime UpdateChannelTransaction(Guid channelId, Guid userId, byte channelTypeId)
        {
            DateTime transaction = new DateTime();
            bool isValid = false;

            if (channelTypeId == (byte)SeedDataEnum.ChannelTypes.Private)
            {
                var firstUserTeams = _vwsDbContext.TeamMembers.Where(teamMemeber => teamMemeber.UserProfileId == userId && !teamMemeber.IsDeleted);
                var secondUserTeams = _vwsDbContext.TeamMembers.Where(teamMemeber => teamMemeber.UserProfileId == channelId && !teamMemeber.IsDeleted);
                
                var commonTeams = firstUserTeams.Select(teamMemeber => teamMemeber.TeamId).Intersect(secondUserTeams.Select(teamMemeber => teamMemeber.TeamId));
                if (commonTeams.Count() == 0)
                    return new DateTime();
                isValid = true;
                var secondUserJoinDates = secondUserTeams.Where(teamMember => commonTeams.Contains(teamMember.TeamId)).Select(teamMemeber => teamMemeber.CreatedOn);
                transaction = secondUserJoinDates.Min();
            }
            else if (channelTypeId == (byte)SeedDataEnum.ChannelTypes.Team)
            {
                var selectedTeam = _vwsDbContext.Teams.FirstOrDefault(team => team.Guid == channelId);
                if (selectedTeam != null)
                {
                    isValid = true;
                    transaction = selectedTeam.CreatedOn;
                }
            }
            else if (channelTypeId == (byte)SeedDataEnum.ChannelTypes.Project)
            {
                var selectedProject = _vwsDbContext.Projects.FirstOrDefault(project => project.Guid == channelId);
                if (selectedProject != null)
                {
                    isValid = true;
                    transaction = selectedProject.CreatedOn;
                }
            }
            else if (channelTypeId == (byte)SeedDataEnum.ChannelTypes.Department)
            {
                var selectedDepartment = _vwsDbContext.Departments.FirstOrDefault(department => department.Guid == channelId);
                if (selectedDepartment != null)
                {
                    isValid = true;
                    transaction = selectedDepartment.CreatedOn;
                }
            }

            if (isValid)
            {
                _vwsDbContext.AddChannelTransaction(new ChannelTransaction()
                {
                    LastTransactionDateTime = transaction,
                    ChannelId = channelId,
                    ChannelTypeId = channelTypeId,
                    UserProfileId = channelTypeId == (byte)SeedDataEnum.ChannelTypes.Private ? userId : (Guid?)null
                });
                _vwsDbContext.Save();
            }

            return transaction;
        }

        private void SetChannelLastTransactionDateTime(ref List<ChannelResponseModel> channelResponseModels)
        {
            var userId = LoggedInUserId.Value;

            foreach (var channelResponseModel in channelResponseModels)
            {
                ChannelTransaction channelTransaction;

                if (channelResponseModel.ChannelTypeId == (byte)SeedDataEnum.ChannelTypes.Private)
                    channelTransaction = _vwsDbContext.ChannelTransactions.FirstOrDefault(transaction => transaction.ChannelTypeId == channelResponseModel.ChannelTypeId &&
                                                                                                        transaction.ChannelId == channelResponseModel.Guid &&
                                                                                                        transaction.UserProfileId == userId);

                else
                    channelTransaction = _vwsDbContext.ChannelTransactions.FirstOrDefault(transaction => transaction.ChannelTypeId == channelResponseModel.ChannelTypeId &&
                                                                                                        transaction.ChannelId == channelResponseModel.Guid);

                if (channelTransaction != null)
                    channelResponseModel.LastTransactionDateTime = channelTransaction.LastTransactionDateTime;
                else
                    channelResponseModel.LastTransactionDateTime = UpdateChannelTransaction(channelResponseModel.Guid, userId, channelResponseModel.ChannelTypeId);
            }
        }

        private void SetChannelUnreadMessages(ref List<ChannelResponseModel> channelResponseModels, List<Guid> userIds)
        {
            for (int i = 0; i < channelResponseModels.Count; i++)
            {
                int readMessagesCount;
                int allMessagesCount;
                Guid channelId = channelResponseModels[i].Guid;

                if (channelResponseModels[i].ChannelTypeId == (byte)SeedDataEnum.ChannelTypes.Private)
                    readMessagesCount = _vwsDbContext.MessageReads.Include(messageRead => messageRead.Message)
                                                                 .Where(messageRead => messageRead.ChannelId == LoggedInUserId.Value && messageRead.Message.FromUserId == userIds[i] && messageRead.ReadBy == LoggedInUserId.Value && !messageRead.Message.IsDeleted)
                                                                 .Count();
                else
                    readMessagesCount = _vwsDbContext.MessageReads.Include(messageRead => messageRead.Message)
                                                                  .Where(messageRead => messageRead.ChannelId == channelId && messageRead.Message.FromUserId != LoggedInUserId.Value && messageRead.ReadBy == LoggedInUserId.Value && !messageRead.Message.IsDeleted)
                                                                  .Count();

                if (channelResponseModels[i].ChannelTypeId == (byte)SeedDataEnum.ChannelTypes.Private)
                    allMessagesCount = _vwsDbContext.Messages.Where(message => message.ChannelId == LoggedInUserId.Value && message.FromUserId == userIds[i] && !message.IsDeleted)
                                                             .Count();
                else
                    allMessagesCount = _vwsDbContext.Messages.Where(message => message.ChannelId == channelId && message.FromUserId != LoggedInUserId.Value && !message.IsDeleted)
                                                                 .Count();

                channelResponseModels[i].NumberOfUnreadMessages = allMessagesCount - readMessagesCount;
            }
        }

        private void SetChannelLastMessage(ref List<ChannelResponseModel> channelResponseModels)
        {
            for (int i = 0; i < channelResponseModels.Count; i++)
            {
                byte channelType = channelResponseModels[i].ChannelTypeId;
                Guid channelId = channelResponseModels[i].Guid;
                Message lastMessage;
                if (channelResponseModels[i].ChannelTypeId == (byte)SeedDataEnum.ChannelTypes.Private)
                {
                    var privateMessages = _vwsDbContext.Messages.Where(message => message.ChannelTypeId == channelType &&
                                                                                ((message.ChannelId == channelId && message.FromUserId == LoggedInUserId) ||
                                                                                (message.ChannelId == LoggedInUserId && message.FromUserId == channelId)) &&
                                                                                !message.IsDeleted).ToList();
                    privateMessages = privateMessages.OrderBy(message => message.SendOn).ToList();
                    lastMessage = privateMessages.Count == 0 ? null : privateMessages[privateMessages.Count - 1];
                }
                else
                {
                    var publicMessages = _vwsDbContext.Messages.Where(message => message.ChannelTypeId == channelType && message.ChannelId == channelId && !message.IsDeleted).ToList();
                    publicMessages = publicMessages.OrderBy(message => message.SendOn).ToList();
                    lastMessage = publicMessages.Count == 0 ? null : publicMessages[publicMessages.Count - 1];
                }
                if (lastMessage == null)
                    continue;
                channelResponseModels[i].LastMessage = new MessageSummary()
                {
                    FromNickName = _vwsDbContext.UserProfiles.FirstOrDefault(profile => profile.UserId == lastMessage.FromUserId).NickName,
                    Body = lastMessage.Body,
                    MessageTypeId = lastMessage.MessageTypeId,
                    FromUserId = lastMessage.FromUserId,
                    SendOn = lastMessage.SendOn
                };
            }
        }

        private List<Guid> GetChannelUserIds(List<ChannelResponseModel> channelResponseModels)
        {
            var result = new List<Guid>();

            foreach (var channelResponseModel in channelResponseModels)
            {
                if (channelResponseModel.ChannelTypeId == (byte)SeedDataEnum.ChannelTypes.Private)
                    result.Add(channelResponseModel.Guid);
                else
                    result.Add(new Guid());
            }

            return result;
        }

        private void ReorderPinnedChannels(ref List<PinnedChannel> pinnedChannels)
        {
            int evenOrder = 2;
            pinnedChannels = pinnedChannels.OrderBy(pinnedChannel => pinnedChannel.EvenOrder).ToList();

            foreach (var pinnedChannel in pinnedChannels)
            {
                pinnedChannel.EvenOrder = evenOrder;
                evenOrder += 2;
            }

            _vwsDbContext.Save();
        }
        #endregion

        #region ChannelAPIS
        [HttpGet]
        [Authorize]
        [Route("getAll")]
        public async Task<IActionResult> GetAll()
        {
            List<ChannelResponseModel> channelResponseModels = new List<ChannelResponseModel>();

            var userId = LoggedInUserId.Value;

            channelResponseModels = await _channelService.GetUserChannels(userId);

            SetChannelsIsMuted(ref channelResponseModels);

            SetChannelIsPinned(ref channelResponseModels);

            SetChannelLastTransactionDateTime(ref channelResponseModels);

            SetChannelUnreadMessages(ref channelResponseModels, GetChannelUserIds(channelResponseModels));

            SetChannelLastMessage(ref channelResponseModels);

            channelResponseModels = channelResponseModels.OrderByDescending(channelResponseModel => channelResponseModel.LastTransactionDateTime).ToList();
            channelResponseModels = channelResponseModels.OrderByDescending(channelResponseModel => channelResponseModel.EvenOrder).ToList();

            return Ok(new ResponseModel<List<ChannelResponseModel>>(channelResponseModels));

        }

        [HttpGet]
        [Authorize]
        [Route("getNumberOfUnreadMessages")]
        public async Task<int> GetNumberOfUnreadMessages()
        {
            var response = new ResponseModel<int>();
            var userId = LoggedInUserId.Value;

            List<ChannelResponseModel> channelResponseModels = new List<ChannelResponseModel>();
            channelResponseModels = await _channelService.GetUserChannels(userId);

            var result = 0;

            foreach (var channelResponseModel in channelResponseModels)
            {
                int readMessagesCount;
                int allMessagesCount;
                if (channelResponseModel.ChannelTypeId == (byte)SeedDataEnum.ChannelTypes.Private)
                    readMessagesCount = _vwsDbContext.MessageReads.Include(messageRead => messageRead.Message)
                                                                  .Where(messageRead => messageRead.ChannelId == userId && messageRead.Message.FromUserId == channelResponseModel.Guid && messageRead.ReadBy == userId && !messageRead.Message.IsDeleted)
                                                                  .Count();
                else
                    readMessagesCount = _vwsDbContext.MessageReads.Include(messageRead => messageRead.Message)
                                                                  .Where(messageRead => messageRead.ChannelId == channelResponseModel.Guid && messageRead.Message.FromUserId != userId && messageRead.ReadBy == LoggedInUserId.Value && !messageRead.Message.IsDeleted)
                                                                  .Count();

                if (channelResponseModel.ChannelTypeId == (byte)SeedDataEnum.ChannelTypes.Private)
                    allMessagesCount = _vwsDbContext.Messages.Where(message => message.ChannelId == LoggedInUserId && message.FromUserId == channelResponseModel.Guid && !message.IsDeleted)
                                                             .Count();
                else
                    allMessagesCount = _vwsDbContext.Messages.Where(message => message.ChannelId == channelResponseModel.Guid && message.FromUserId != userId && !message.IsDeleted)
                                                             .Count();

                result += allMessagesCount - readMessagesCount;
            }

            return result;
        }
        #endregion

        #region MuteAPIS
        [HttpPut]
        [Authorize]
        [Route("muteChannel")]
        public async Task<IActionResult> MuteChannel([FromBody] MuteChannelModel model)
        {
            var response = new ResponseModel();
            var userId = LoggedInUserId.Value;

            var muteUntil = DateTime.UtcNow.AddMinutes(model.MuteMinutes);

            if (!_channelService.DoesChannelExist(model.ChannelId, model.ChannelTypeId))
            {
                response.AddError(_localizer["There is no channel with given information."]);
                response.Message = "Channel not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_channelService.HasUserAccessToChannel(userId, model.ChannelId, model.ChannelTypeId))
            {
                response.AddError(_localizer["You do not have access to this channel."]);
                response.Message = "Channel access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            var selectedMutedChannel = _vwsDbContext.MutedChannels.FirstOrDefault(mutedChannels => mutedChannels.ChannelId == model.ChannelId &&
                                                                                                  mutedChannels.UserId == userId &&
                                                                                                  mutedChannels.ChannelTypeId == model.ChannelTypeId);

            if (selectedMutedChannel != null)
            {
                selectedMutedChannel.ForEver = model.ForEver;
                selectedMutedChannel.IsMuted = true;
                selectedMutedChannel.MuteUntil = muteUntil;
            }
            else
            {
                var newMutedChannel = new MutedChannel()
                {
                    ChannelId = model.ChannelId,
                    ChannelTypeId = model.ChannelTypeId,
                    ForEver = model.ForEver,
                    IsMuted = true,
                    UserId = userId,
                    MuteUntil = muteUntil
                };
                await _vwsDbContext.AddMutedChannelAsync(newMutedChannel);
            }
            _vwsDbContext.Save();

            response.Message = "Channel muted successfully!";
            return Ok(response);
        }

        [HttpPut]
        [Authorize]
        [Route("unmuteChannel")]
        public IActionResult UnmuteChannel([FromBody] UnmuteChannelModel model)
        {
            var userId = LoggedInUserId.Value;
            var response = new ResponseModel();

            if (!_channelService.DoesChannelExist(model.ChannelId, model.ChannelTypeId))
            {
                response.AddError(_localizer["There is no channel with given information."]);
                response.Message = "Channel not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_channelService.HasUserAccessToChannel(userId, model.ChannelId, model.ChannelTypeId))
            {
                response.AddError(_localizer["You do not have access to this channel."]);
                response.Message = "Channel access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            var selectedMutedChannel = _vwsDbContext.MutedChannels.FirstOrDefault(mutedChannels => mutedChannels.ChannelId == model.ChannelId &&
                                                                                                  mutedChannels.UserId == userId &&
                                                                                                  mutedChannels.ChannelTypeId == model.ChannelTypeId);

            if (selectedMutedChannel == null)
            {
                response.AddError(_localizer["Channel is not muted."]);
                response.Message = "Channel is not muted";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            selectedMutedChannel.IsMuted = false;
            selectedMutedChannel.ForEver = false;
            _vwsDbContext.Save();

            response.Message = "Channel unmuted successfully!";
            return Ok(response);
        }
        #endregion

        #region PinAPIS
        [HttpPut]
        [Authorize]
        [Route("pinChannel")]
        public IActionResult PinChannel([FromBody] PinChannelModel model)
        {
            var userId = LoggedInUserId.Value;
            var response = new ResponseModel();

            if (!_channelService.DoesChannelExist(model.ChannelId, model.ChannelTypeId))
            {
                response.AddError(_localizer["There is no channel with given information."]);
                response.Message = "Channel not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_channelService.HasUserAccessToChannel(userId, model.ChannelId, model.ChannelTypeId))
            {
                response.AddError(_localizer["You do not have access to this channel."]);
                response.Message = "Channel access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            var selectedPinnedChannel = _vwsDbContext.PinnedChannels.FirstOrDefault(pinnedChannel => pinnedChannel.ChannelId == model.ChannelId &&
                                                                                                    pinnedChannel.ChannelTypeId == model.ChannelTypeId &&
                                                                                                    pinnedChannel.UserId == userId);

            if (selectedPinnedChannel != null)
            {
                response.AddError(_localizer["Channel is already pinned."]);
                response.Message = "Channel is already pinned";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            var userPinnedChannels = _vwsDbContext.PinnedChannels.Where(pinnedChannel => pinnedChannel.UserId == userId)
                                                                .OrderByDescending(userPinnedChannel => userPinnedChannel.EvenOrder).
                                                                ToList();

            int last = 0;
            if (userPinnedChannels.Count != 0)
                last = userPinnedChannels[0].EvenOrder;

            var newPinnedChannel = new PinnedChannel()
            {
                ChannelId = model.ChannelId,
                ChannelTypeId = model.ChannelTypeId,
                EvenOrder = last + 2,
                UserId = userId
            };

            _vwsDbContext.AddPinnedChannel(newPinnedChannel);
            _vwsDbContext.Save();

            response.Message = "Channel pinned successfully!";
            return Ok(response);
        }

        [HttpPut]
        [Authorize]
        [Route("unpinChannel")]
        public IActionResult UnpinChannel([FromBody] PinChannelModel model)
        {
            var userId = LoggedInUserId.Value;
            var response = new ResponseModel();

            if (!_channelService.DoesChannelExist(model.ChannelId, model.ChannelTypeId))
            {
                response.AddError(_localizer["There is no channel with given information."]);
                response.Message = "Channel not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_channelService.HasUserAccessToChannel(userId, model.ChannelId, model.ChannelTypeId))
            {
                response.AddError(_localizer["You do not have access to this channel."]);
                response.Message = "Channel access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            var selectedPinnedChannel = _vwsDbContext.PinnedChannels.FirstOrDefault(pinnedChannel => pinnedChannel.ChannelId == model.ChannelId &&
                                                                                                    pinnedChannel.ChannelTypeId == model.ChannelTypeId &&
                                                                                                    pinnedChannel.UserId == userId);

            if (selectedPinnedChannel == null)
            {
                response.AddError(_localizer["Channel have not been pinned."]);
                response.Message = "Channel have not been pinned.";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            _vwsDbContext.DeletePinnedChannel(selectedPinnedChannel);
            _vwsDbContext.Save();

            var userPinnedChannels = _vwsDbContext.PinnedChannels.Where(pinnedChannel => pinnedChannel.UserId == userId)
                                                                .OrderByDescending(userPinnedChannel => userPinnedChannel.EvenOrder).
                                                                ToList();

            ReorderPinnedChannels(ref userPinnedChannels);

            response.Message = "Channel unpinned successfully!";
            return Ok(response);
        }
        #endregion

        #region ChannelMemverAPIS
        [HttpGet]
        [Authorize]
        [Route("getChannelMembers")]
        public async Task<IActionResult> GetChannelMembers(Guid channelId, byte channelTypeId)
        {
            var response = new ResponseModel<List<UserModel>>();
            var members = new List<UserModel>();

            var userId = LoggedInUserId.Value;

            if (!_channelService.DoesChannelExist(channelId, channelTypeId))
            {
                response.AddError(_localizer["There is no channel with given information."]);
                response.Message = "Channel not found";
                return StatusCode(StatusCodes.Status400BadRequest, response);
            }

            if (!_channelService.HasUserAccessToChannel(userId, channelId, channelTypeId))
            {
                response.AddError(_localizer["You do not have access to this channel."]);
                response.Message = "Channel access denied";
                return StatusCode(StatusCodes.Status403Forbidden, response);
            }

            List<UserProfile> users = new List<UserProfile>();

            if (channelTypeId == (byte)SeedDataEnum.ChannelTypes.Private)
            {
                var userProfile = await _vwsDbContext.GetUserProfileAsync(userId);
                var otherUserProfile = await _vwsDbContext.GetUserProfileAsync(channelId);
                members.Add(new UserModel()
                {
                    ProfileImageGuid = userProfile.ProfileImageGuid,
                    UserId = userId,
                    NickName = userProfile.NickName
                });
                members.Add(new UserModel()
                {
                    ProfileImageGuid = otherUserProfile.ProfileImageGuid,
                    UserId = channelId,
                    NickName = otherUserProfile.NickName
                });

                response.Value = members;
                response.Message = "Members returned successfully!";
                return Ok(response);
            }

            else if (channelTypeId == (byte)SeedDataEnum.ChannelTypes.Team)
            {
                var selectedTeam = _vwsDbContext.Teams.FirstOrDefault(team => team.Guid == channelId);
                users = _vwsDbContext.TeamMembers.Include(teamMember => teamMember.UserProfile)
                                                .Where(teamMember => teamMember.TeamId == selectedTeam.Id && !teamMember.IsDeleted)
                                                .Select(teamMember => teamMember.UserProfile).ToList();
            }

            else if (channelTypeId == (byte)SeedDataEnum.ChannelTypes.Department)
            {
                var selectedDepartment = _vwsDbContext.Departments.FirstOrDefault(department => department.Guid == channelId);
                users = _vwsDbContext.DepartmentMembers.Include(departmentMember => departmentMember.UserProfile)
                                                      .Where(departmentMember => departmentMember.DepartmentId == selectedDepartment.Id && !departmentMember.IsDeleted)
                                                      .Select(departmentMember => departmentMember.UserProfile).ToList();
            }

            else if (channelTypeId == (byte)SeedDataEnum.ChannelTypes.Project)
            {
                var selectedProject = _vwsDbContext.Projects.Include(project => project.ProjectDepartments).FirstOrDefault(project => project.Guid == channelId);
                if (selectedProject.TeamId == null)
                {
                    users = _vwsDbContext.ProjectMembers.Include(projectMember => projectMember.UserProfile)
                                                       .Where(projectMember => projectMember.ProjectId == selectedProject.Id && !projectMember.IsDeleted)
                                                       .Select(projectMember => projectMember.UserProfile).ToList();

                }
                else if (selectedProject.ProjectDepartments.Count == 0)
                {
                    users = _vwsDbContext.TeamMembers.Include(teamMember => teamMember.UserProfile)
                                                    .Where(teamMember => teamMember.TeamId == selectedProject.TeamId && !teamMember.IsDeleted)
                                                    .Select(teamMember => teamMember.UserProfile).ToList();
                }
                else
                {
                    foreach (var projectDepartment in selectedProject.ProjectDepartments)
                    {
                        var selectedDepartment = _vwsDbContext.Departments.FirstOrDefault(department => department.Id == projectDepartment.DepartmentId);
                        users.AddRange(_vwsDbContext.DepartmentMembers.Include(departmentMember => departmentMember.UserProfile)
                                                                     .Where(departmentMember => departmentMember.DepartmentId == selectedDepartment.Id && !departmentMember.IsDeleted)
                                                                     .Select(departmentMember => departmentMember.UserProfile));
                    }
                }
            }

            foreach (var user in users)
            {
                members.Add(new UserModel()
                {
                    UserId = user.UserId,
                    NickName = user.NickName,
                    ProfileImageGuid = user.ProfileImageGuid
                });
            }

            response.Message = "Members returned successfully!";
            response.Value = members;

            return Ok(response);
        }
        #endregion
    }
}
