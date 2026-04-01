/**
 * Theme: Adminto - Responsive Bootstrap 5 Admin Dashboard
 * Author: Coderthemes
 * Module/App: Form Wizard
 */

window.loadWizard = function () {
  new Wizard("#basicwizard");

  new Wizard("#progressbarwizard", {
    progress: true,
  });

  new Wizard("#validation-wizard", {
    validate: true,
  });
};
