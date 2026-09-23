FROM --platform=linux/arm64 public.ecr.aws/lambda/dotnet:8

# copy app binaries
COPY bin/publish/. /var/task

# set this to the fully qualified class name of your function handler
CMD [ "Pdc.Mobile.ItemCutoffs.Aws.Lambda::Pdc.Mobile.ItemCutoffs.Aws.Lambda.Function::FunctionHandler" ]
