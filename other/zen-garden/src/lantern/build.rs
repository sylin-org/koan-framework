fn main() {
    // Capture build timestamp from environment or use "dev"
    let build_number = std::env::var("CARGO_BUILD_NUMBER").unwrap_or_else(|_| "dev".to_string());
    println!("cargo:rustc-env=BUILD_NUMBER={}", build_number);
    
    // Re-run if environment variable changes
    println!("cargo:rerun-if-env-changed=CARGO_BUILD_NUMBER");
}
