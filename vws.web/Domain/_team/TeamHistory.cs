﻿using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace vws.web.Domain._team
{
    [Table("Team_TeamHistory")]
    public class TeamHistory
    {
        public long Id { get; set; }

        public int TeamId { get; set; }

        public string Event { get; set; }

        public string CommaSepratedParameters { get; set; }

        public DateTime EventTime { get; set; }
    }
}
