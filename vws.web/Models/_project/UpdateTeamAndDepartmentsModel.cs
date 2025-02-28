﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace vws.web.Models._project
{
    public class UpdateTeamAndDepartmentsModel
    {
        [Required]
        public int Id { get; set; }
        public int? TeamId { get; set; }
        [Required]
        public List<int> DepartmentIds { get; set; }
    }
}
