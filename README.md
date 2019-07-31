# SenseNet-ExternalData-Representation
Toying with various implementations of represent external feeds as sensenet repository content

# Idea behind it
These are content handlers to fetch data from external feed via webrequest and store the response in it's own binary in sensenet repository. This code is need heavy review and refactor since sn has split services and webpages. This solution should not depend on webpages module but it does for now. I haven't tested it for a while so consider it as an in progress repo..

# Ho it works
1. install ctd for selected logic
1. deploy assembly 
1. create content with installed content type
1. set external url, sync interval and probably other settings
1. fetch Content as usual with other normal sensenet Contents, it should contain external feed it's own binary
