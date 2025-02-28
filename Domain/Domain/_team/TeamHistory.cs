﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace vws.web.Domain._team
{
    [Table("Team_TeamHistory")]
    public class TeamHistory
    {
        public TeamHistory()
        {
            TeamHistoryParameters = new HashSet<TeamHistoryParameter>();
        }

        public long Id { get; set; }

        public int TeamId { get; set; }

        public string EventBody { get; set; }

        public DateTime EventTime { get; set; }

        public virtual Team Team { get; set; }

        public virtual ICollection<TeamHistoryParameter> TeamHistoryParameters { get; set; }
    }
}
