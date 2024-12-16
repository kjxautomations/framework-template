using System;

namespace KJX.ProjectTemplate.Engineering.Models;

public class NavigationMenuType
{

    public NavigationMenuType(Type type)
    {
        ViewModel = type;
    }

    public string MenuLabel { get; set; }
    public Type ViewModel { get; }
}