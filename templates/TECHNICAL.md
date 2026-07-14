# Template package technical contract

`Sylin.Koan.Templates` is a content-only, independently versioned package. Any committed change under
`templates/` mints a new package version when it reaches `dev`. The release compiler validates and
publishes it without expecting managed build output or a symbol package.
