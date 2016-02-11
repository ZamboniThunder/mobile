using System;
using Cirrious.FluentLayouts.Touch;
using Toggl.Phoebe.Analytics;
using Toggl.Phoebe.Data;
using Toggl.Phoebe.Logging;
using Toggl.Phoebe.Net;
using Toggl.Ross.Theme;
using Toggl.Ross.Views;
using UIKit;
using XPlatUtils;

namespace Toggl.Ross.ViewControllers
{
    public class LoginViewController : UIViewController
    {
        private const string Tag = "LoginViewController";

        private UIView inputsContainer;
        private UIView topBorder;
        private UIView middleBorder;
        private UIView bottomBorder;
        private UITextField emailTextField;
        private UITextField passwordTextField;
        private UIButton passwordActionButton;

        public LoginViewController ()
        {
            Title = "LoginTitle".Tr ();
        }

        public override void LoadView ()
        {
            View = new UIView ()
            .Apply (Style.Screen);

            View.Add (inputsContainer = new UIView ().Apply (Style.Login.InputsContainer));

            inputsContainer.Add (topBorder = new UIView ().Apply (Style.Login.InputsBorder));

            inputsContainer.Add (emailTextField = new UITextField () {
                Placeholder = "LoginEmailHint".Tr (),
                AutocapitalizationType = UITextAutocapitalizationType.None,
                KeyboardType = UIKeyboardType.EmailAddress,
                ReturnKeyType = UIReturnKeyType.Next,
                ClearButtonMode = UITextFieldViewMode.Always,
                ShouldReturn = HandleShouldReturn,
                AutocorrectionType = UITextAutocorrectionType.No
            } .Apply (Style.Login.EmailField));

            inputsContainer.Add (middleBorder = new UIView ().Apply (Style.Login.InputsBorder));

            inputsContainer.Add (passwordTextField = new PasswordTextField () {
                Placeholder = "LoginPasswordHint".Tr (),
                AutocapitalizationType = UITextAutocapitalizationType.None,
                AutocorrectionType = UITextAutocorrectionType.No,
                SecureTextEntry = true,
                ReturnKeyType = UIReturnKeyType.Go,
                ShouldReturn = HandleShouldReturn,
            } .Apply (Style.Login.PasswordField));

            inputsContainer.Add (bottomBorder = new UIView ().Apply (Style.Login.InputsBorder));

            View.Add (passwordActionButton = new UIButton ()
            .Apply (Style.Login.LoginButton));
            passwordActionButton.SetTitle ("LoginLoginButtonText".Tr (), UIControlState.Normal);
            passwordActionButton.TouchUpInside += OnPasswordActionButtonTouchUpInside;

            inputsContainer.AddConstraints (
                topBorder.AtTopOf (inputsContainer),
                topBorder.AtLeftOf (inputsContainer),
                topBorder.AtRightOf (inputsContainer),
                topBorder.Height ().EqualTo (1f),

                emailTextField.Below (topBorder),
                emailTextField.AtLeftOf (inputsContainer, 20f),
                emailTextField.AtRightOf (inputsContainer, 10f),
                emailTextField.Height ().EqualTo (42f),

                middleBorder.Below (emailTextField),
                middleBorder.AtLeftOf (inputsContainer, 20f),
                middleBorder.AtRightOf (inputsContainer),
                middleBorder.Height ().EqualTo (1f),

                passwordTextField.Below (middleBorder),
                passwordTextField.AtLeftOf (inputsContainer, 20f),
                passwordTextField.AtRightOf (inputsContainer),
                passwordTextField.Height ().EqualTo (42f),

                bottomBorder.Below (passwordTextField),
                bottomBorder.AtLeftOf (inputsContainer),
                bottomBorder.AtRightOf (inputsContainer),
                bottomBorder.AtBottomOf (inputsContainer),
                bottomBorder.Height ().EqualTo (1f)
            );

            inputsContainer.SubviewsDoNotTranslateAutoresizingMaskIntoConstraints ();

            View.AddConstraints (
                inputsContainer.AtTopOf (View, 80f),
                inputsContainer.AtLeftOf (View),
                inputsContainer.AtRightOf (View),

                passwordActionButton.Below (inputsContainer, 20f),
                passwordActionButton.AtLeftOf (View),
                passwordActionButton.AtRightOf (View),
                passwordActionButton.Height ().EqualTo (60f)
            );

            View.SubviewsDoNotTranslateAutoresizingMaskIntoConstraints ();
        }

        public override void ViewDidAppear (bool animated)
        {
            base.ViewDidAppear (animated);
            emailTextField.BecomeFirstResponder ();

            ServiceContainer.Resolve<ITracker> ().CurrentScreen = "Login";
        }

        private bool HandleShouldReturn (UITextField textField)
        {
            if (textField == emailTextField) {
                passwordTextField.BecomeFirstResponder ();
            } else if (textField == passwordTextField) {
                textField.ResignFirstResponder ();
                TryPasswordAuth ();
            } else {
                return false;
            }
            return true;
        }

        private void OnPasswordActionButtonTouchUpInside (object sender, EventArgs e)
        {
            TryPasswordAuth ();
        }

        private async void TryPasswordAuth ()
        {
            // Small UI trick to permit OBM testers
            // interact with the staging API
            if (emailTextField.Text == "staging") {
                var isStaging = ServiceContainer.Resolve<ISettingsStore> ().IsStagingMode;
                ServiceContainer.Resolve<ISettingsStore> ().IsStagingMode = !isStaging;
                var msg = isStaging ? "You're in Normal Mode" : "You're in Staging Mode";
                var alertView = new UIAlertView ("Staging Mode", msg + "\nRestart the app to continue.", null, "Ok");
                alertView.Show ();
                return;
            }

            if (IsAuthenticating) {
                return;
            }

            IsAuthenticating = true;

            try {
                var authManager = ServiceContainer.Resolve<AuthManager> ();
                var authRes = await authManager.AuthenticateAsync (emailTextField.Text, passwordTextField.Text);

                if (authRes != AuthResult.Success) {
                    AuthErrorAlert.Show (this, emailTextField.Text, authRes, AuthErrorAlert.Mode.Login);
                } else {
                    // Start the initial sync for the user
                    ServiceContainer.Resolve<ISyncManager> ().Run (SyncMode.Full);
                }
            } catch (InvalidOperationException ex) {
                var log = ServiceContainer.Resolve<ILogger> ();
                log.Info (Tag, ex, "Failed to authenticate (password) the user.");
            } finally {
                IsAuthenticating = false;
            }
        }

        private bool isAuthenticating;

        private bool IsAuthenticating
        {
            get { return isAuthenticating; }
            set {
                isAuthenticating = value;
                emailTextField.Enabled = !isAuthenticating;
                passwordTextField.Enabled = !isAuthenticating;
                passwordActionButton.Enabled = !isAuthenticating;

                passwordActionButton.SetTitle ("LoginLoginProgressText".Tr (), UIControlState.Disabled);
            }
        }
    }
}
