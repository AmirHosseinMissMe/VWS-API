﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using vws.web.Domain._calendar;
using vws.web.Domain._department;
using vws.web.Domain._file;
using vws.web.Domain._task;
using vws.web.Domain._team;

namespace vws.web.Domain._project
{
    [Table("Project_Project")]
    public class Project
    {
        public Project()
        {
            ProjectMembers = new HashSet<ProjectMember>();
            ProjectDepartments = new HashSet<ProjectDepartment>();
            Tasks = new HashSet<GeneralTask>();
            TaskStatuses = new HashSet<TaskStatus>();
            EventProjects = new HashSet<EventProject>();
        }

        public int Id { get; set; }

        public Guid Guid { get; set; }

        [MaxLength(500)]
        public string Name { get; set; }

        [MaxLength(2000)]
        public string Description { get; set; }

        public byte StatusId { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        [MaxLength(6)]
        public string Color { get; set; }

        public bool IsDeleted { get; set;}

        public Guid CreatedBy { get; set; }

        public Guid ModifiedBy { get; set; }

        public DateTime CreatedOn { get; set; }

        public DateTime ModifiedOn { get; set; }

        [ForeignKey("Team")]
        public int? TeamId { get; set; }

        [ForeignKey("ProjectImage")]
        public int? ProjectImageId { get; set; }

        public Guid? ProjectImageGuid { get; set; }

        public virtual FileContainer ProjectImage { get; set; }

        public virtual Team Team { get; set; }

        public virtual ProjectStatus Status { get; set; }

        public virtual ICollection<ProjectMember> ProjectMembers { get; set; }

        public virtual ICollection<ProjectDepartment> ProjectDepartments { get; set; }

        public virtual ICollection<GeneralTask> Tasks { get; set; }

        public virtual ICollection<TaskStatus> TaskStatuses { get; set; }

        public virtual ICollection<EventProject> EventProjects { get; set; }
    }

    class ProjectComparer : IEqualityComparer<Project>
    {
        public bool Equals(Project firstProject, Project secondProject)
        {

            if (Object.ReferenceEquals(firstProject, secondProject)) return true;

            if (Object.ReferenceEquals(firstProject, null) || Object.ReferenceEquals(secondProject, null))
                return false;

            return firstProject.Id == secondProject.Id;
        }

        public int GetHashCode(Project project)
        {
            if (Object.ReferenceEquals(project, null)) return 0;

            return project.Id.GetHashCode();
        }
    }
}
