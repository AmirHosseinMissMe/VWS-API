﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace vws.web.Models._project
{
    public class ProjectResponseModel
    {
        public int Id { get; set; }
        public Guid Guid { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public byte StatusId { get; set; }
        public long NumberOfUpdates { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime ModifiedOn { get; set; }
        public UserModel CreatedBy { get; set; }
        public UserModel ModifiedBy { get; set; }
        public string Color { get; set; }
        public int? TeamId { get; set; }
        public string TeamName { get; set; }
        public List<UserModel> Users { get; set; }
        public List<int> DepartmentIds { get; set; }
        public Guid? ProjectImageGuid { get; set; }
        public long NumberOfTasks { get; set; }
        public double SpentTimeInMinutes { get; set; }
    }
}
