
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Abstractions;
using ReactiveUI.Validation.Contexts;
using ValidationContext = ReactiveUI.Validation.Contexts.ValidationContext;
using KJX.Core.Interfaces;

namespace KJX.Core.ViewModels;

public abstract class StateViewModelBase<TState, TTrigger> : ViewModelBase, IValidatableViewModel
    where TState : System.Enum 
    where TTrigger : System.Enum
{
    private readonly INavigationService<TState, TTrigger> _navigationService;
    [Reactive] public bool ViewVisible { get; set; }
    public IValidationContext ValidationContext { get; } = new ValidationContext();
    
    [Reactive]
    protected TState CurrentState { get; private set; }

    protected StateViewModelBase(INavigationService<TState, TTrigger> navigationService,
        IEnumerable<TState> statesShowingView)
    {
        _statesShowingView = new List<TState>(statesShowingView);
        _navigationService = navigationService;
        var callback =
            new Action<TState>(state =>
            {
                ViewVisible = _statesShowingView.Contains(state);
                CurrentState = state;
            });
        _navigationService.WhenAnyValue(x => x.CurrentState)
            .Subscribe(callback);
    }

    private readonly List<TState> _statesShowingView;
    
    public virtual void Reset()
    {
    }
}