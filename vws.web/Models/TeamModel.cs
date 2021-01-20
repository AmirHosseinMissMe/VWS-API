﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace vws.web.Models
{
    public class TeamModel
    {
        [Required]
        public string Name { get; set; }
        public string Description { get; set; }
        public string Color { get; set; }
    }
}
