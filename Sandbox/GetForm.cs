﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Sandbox
{
    using Simple.Web;

    [UriTemplate("/form")]
    public class GetForm : IGet, ISpecifyView
    {
        public string Title { get { return "Test Form"; } }

        public Status Get()
        {
            return 200;
        }

        public string ViewPath
        {
            get { return "Form"; }
        }
    }

    public class Form
    {
        public string Text { get; set; }
    }
}